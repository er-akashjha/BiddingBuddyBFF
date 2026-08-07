using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Billing;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Promo code validation + discount math. ALL money computation is here, server-side —
/// the client only ever displays figures this service produced.
/// </summary>
public class PromoService(BffDbContext db) : IPromoService
{
    // Razorpay refuses orders under ₹1; a 100%-flat code must not produce a ₹0 order.
    private const long MinimumChargePaise = 100;

    public async Task<PromoValidationDto> ValidateAsync(
        Guid orgId, string code, string planCode, CancellationToken ct = default)
    {
        var def = PlanCatalog.Get(planCode);
        if (!PlanCatalog.IsPaid(def.Code))
            return new PromoValidationDto(false, "NOT_APPLICABLE");

        var (promo, error) = await ResolveCodeAsync(orgId, code, def.Code, ct);
        if (promo is null)
            return new PromoValidationDto(false, error);

        var amount = def.PricePaiseAnnual;
        var discount = ComputeDiscount(promo, amount);

        return new PromoValidationDto(
            Valid: true, Error: null,
            Code: promo.Code,
            Description: promo.Description,
            DiscountType: promo.DiscountType,
            DiscountValue: promo.DiscountValue,
            IsLifetime: promo.IsLifetime,
            OriginalAmountPaise: amount,
            DiscountPaise: discount,
            FinalAmountPaise: amount - discount);
    }

    public async Task<PromoApplication?> ResolveForCheckoutAsync(
        Guid orgId, string? code, string planCode, long amountPaise, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var (promo, error) = await ResolveCodeAsync(orgId, code!, planCode, ct);
            if (promo is null)
                // An explicitly entered code that stopped being valid must fail the
                // checkout loudly, not silently charge full price.
                throw new InvalidOperationException(error ?? "INVALID_CODE");
            return new PromoApplication(promo.Id, promo.Code, promo.IsLifetime, ComputeDiscount(promo, amountPaise));
        }

        // No code entered — auto-apply an earned lifetime discount (founding member etc.).
        var lifetime = await BestLifetimePromoAsync(orgId, ct);
        if (lifetime is null) return null;

        // A lifetime redemption already consumed its (promo, org) slot; plan and validity
        // windows no longer gate it — the promise was "for life".
        return new PromoApplication(lifetime.Id, lifetime.Code, IsLifetime: true, ComputeDiscount(lifetime, amountPaise));
    }

    public async Task<LifetimeDiscountDto?> GetActiveLifetimeDiscountAsync(
        Guid orgId, CancellationToken ct = default)
    {
        var promo = await BestLifetimePromoAsync(orgId, ct);
        return promo is null ? null : new LifetimeDiscountDto(promo.Code, promo.DiscountType, promo.DiscountValue);
    }

    // ── internals ────────────────────────────────────────────────────────────

    private async Task<(PromoCode? Promo, string? Error)> ResolveCodeAsync(
        Guid orgId, string code, string planCode, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length == 0) return (null, "INVALID_CODE");

        var promo = await db.PromoCodes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == normalized, ct);

        var now = DateTime.UtcNow;
        if (promo is null || !promo.IsActive) return (null, "INVALID_CODE");
        if (promo.ValidFrom is { } from && from > now) return (null, "INVALID_CODE");
        if (promo.ValidUntil is { } until && until < now) return (null, "EXPIRED");
        if (promo.MaxRedemptions is { } max && promo.RedeemedCount >= max) return (null, "EXHAUSTED");
        if (promo.AppliesToPlans is { Length: > 0 } plans && !plans.Contains(planCode)) return (null, "NOT_APPLICABLE");

        var alreadyUsed = await db.PromoRedemptions.AsNoTracking()
            .AnyAsync(r => r.PromoId == promo.Id && r.OrgId == orgId, ct);
        if (alreadyUsed) return (null, "ALREADY_USED");

        return (promo, null);
    }

    private async Task<PromoCode?> BestLifetimePromoAsync(Guid orgId, CancellationToken ct)
    {
        var promos = await db.PromoRedemptions.AsNoTracking()
            .Where(r => r.OrgId == orgId && r.IsLifetime)
            .Join(db.PromoCodes.AsNoTracking(), r => r.PromoId, p => p.Id, (r, p) => p)
            .ToListAsync(ct);

        // "Best" across types is amount-dependent; in practice one lifetime code per org.
        // Deterministic preference: percent codes first, then the largest value.
        return promos
            .OrderByDescending(p => p.DiscountType == "percent")
            .ThenByDescending(p => p.DiscountValue)
            .FirstOrDefault();
    }

    private static long ComputeDiscount(PromoCode promo, long amountPaise)
    {
        var discount = promo.DiscountType == "percent"
            ? amountPaise * promo.DiscountValue / 100
            : promo.DiscountValue;

        return Math.Clamp(discount, 0, Math.Max(0, amountPaise - MinimumChargePaise));
    }
}
