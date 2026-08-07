using System.Globalization;
using System.Text.Json;
using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Billing;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Subscription checkout and activation.
///
/// <para><b>Activation is idempotent by design.</b> The browser's verify callback and
/// Razorpay's webhook race each other on every successful payment; both funnel into
/// <see cref="ActivateFromPaymentAsync"/>, which locks the billing_payments row by its
/// unique razorpay_order_id and no-ops when the row is already captured. Whichever
/// arrives second changes nothing.</para>
/// </summary>
public class BillingService(
    BffDbContext db,
    IRazorpayClient razorpay,
    IPromoService promos,
    IPlanService planService,
    IAiQuotaService aiQuota,
    INotificationPublisher notifications,
    INotificationAudienceResolver audience,
    IOptions<RazorpayOptions> razorpayOptions,
    IConfiguration config,
    ILogger<BillingService> log) : IBillingService
{
    private readonly RazorpayOptions _razorpay = razorpayOptions.Value;

    private string BillingUrl =>
        $"{(config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/')}/billing";

    public async Task<BillingSummaryDto> GetSummaryAsync(Guid orgId, CancellationToken ct = default)
    {
        var plan = await planService.GetPlanForAsync(orgId, ct);
        var seatsUsed = await db.OrgMembers.CountAsync(m => m.OrgId == orgId && m.Status == "active", ct);
        var (aiUsed, aiQuotaValue) = await aiQuota.GetUsageAsync(orgId, PlanFeatures.AiSummary, ct);
        var lifetime = await promos.GetActiveLifetimeDiscountAsync(orgId, ct);

        return new BillingSummaryDto(
            plan.PlanCode, plan.PlanName, plan.Status,
            plan.TrialEndsAt, plan.PeriodEnd,
            seatsUsed, plan.SeatCap,
            aiUsed, aiQuotaValue,
            _razorpay.IsConfigured,
            lifetime);
    }

    public async Task<CheckoutOrderDto> CreateCheckoutAsync(
        Guid orgId, Guid userId, CreateCheckoutDto dto, CancellationToken ct = default)
    {
        if (!_razorpay.IsConfigured)
            throw new InvalidOperationException("CHECKOUT_UNAVAILABLE");

        var def = PlanCatalog.Get(dto.PlanCode);
        if (!PlanCatalog.IsPaid(def.Code) || !def.Code.Equals(dto.PlanCode, StringComparison.Ordinal))
            throw new InvalidOperationException("INVALID_PLAN");

        // Monthly billing is v2 — the pricing page ships its toggle disabled, and this is
        // the server-side half of that promise.
        if (!string.Equals(dto.Cycle, "annual", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("INVALID_CYCLE");

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        var original = def.PricePaiseAnnual;
        var promo = await promos.ResolveForCheckoutAsync(orgId, dto.PromoCode, def.Code, original, ct);
        var amount = original - (promo?.DiscountPaise ?? 0);

        var payment = new BillingPayment
        {
            Id                  = Guid.NewGuid(),
            OrgId               = orgId,
            PlanCode            = def.Code,
            Cycle               = "annual",
            AmountPaise         = amount,
            Currency            = "INR",
            PromoCodeId         = promo?.PromoId,
            OriginalAmountPaise = promo is null ? null : original,
            DiscountPaise       = promo?.DiscountPaise,
            Status              = "created",
            CreatedBy           = userId,
            CreatedAt           = DateTime.UtcNow,
        };
        db.BillingPayments.Add(payment);
        await db.SaveChangesAsync(ct);

        var order = await razorpay.CreateOrderAsync(
            amount, "INR", payment.Id.ToString(),
            new Dictionary<string, string>
            {
                ["org_id"]    = orgId.ToString(),
                ["plan_code"] = def.Code,
                ["payment_id"] = payment.Id.ToString(),
            }, ct);

        payment.RazorpayOrderId = order.Id;
        await db.SaveChangesAsync(ct);

        return new CheckoutOrderDto(
            payment.Id, order.Id, amount,
            payment.OriginalAmountPaise, payment.DiscountPaise, promo?.Code,
            "INR", _razorpay.KeyId, def.Name, org.Name);
    }

    public async Task<BillingSummaryDto> VerifyAndActivateAsync(
        Guid orgId, VerifyPaymentDto dto, CancellationToken ct = default)
    {
        if (!RazorpaySignature.VerifyCheckout(
                dto.RazorpayOrderId, dto.RazorpayPaymentId, dto.RazorpaySignature, _razorpay.KeySecret))
        {
            log.LogWarning("Razorpay checkout signature mismatch for order {OrderId} (org {OrgId})",
                dto.RazorpayOrderId, orgId);
            throw new UnauthorizedAccessException("Payment signature verification failed.");
        }

        // The order must belong to the calling org — a valid signature proves the payment
        // is genuine, not that it was made by whoever is asking us to credit it.
        var owner = await db.BillingPayments.AsNoTracking()
            .Where(p => p.RazorpayOrderId == dto.RazorpayOrderId)
            .Select(p => (Guid?)p.OrgId)
            .FirstOrDefaultAsync(ct);
        if (owner is null) throw new KeyNotFoundException("Payment not found.");
        if (owner != orgId) throw new UnauthorizedAccessException("This payment belongs to another organization.");

        await ActivateFromPaymentAsync(dto.RazorpayOrderId, dto.RazorpayPaymentId, "checkout", ct);
        return await GetSummaryAsync(orgId, ct);
    }

    public async Task ActivateFromPaymentAsync(
        string razorpayOrderId, string razorpayPaymentId, string source, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE: the verify callback and the webhook arrive within milliseconds of
        // each other. Without the lock both would read status='created' and both activate.
        var payment = await db.BillingPayments
            .FromSql($"SELECT * FROM billing_payments WHERE razorpay_order_id = {razorpayOrderId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

        if (payment is null)
        {
            log.LogWarning("Activation for unknown Razorpay order {OrderId} (source {Source})",
                razorpayOrderId, source);
            return;
        }

        if (payment.Status == "captured")
        {
            log.LogInformation("Payment {OrderId} already captured — {Source} activation is a no-op.",
                razorpayOrderId, source);
            return;
        }

        var now = DateTime.UtcNow;
        payment.Status            = "captured";
        payment.RazorpayPaymentId = razorpayPaymentId;
        payment.SignatureVerified = true;
        payment.CapturedAt        = now;

        var sub = await db.OrgSubscriptions.FirstOrDefaultAsync(s => s.OrgId == payment.OrgId, ct);
        if (sub is null)
        {
            sub = new OrgSubscription { Id = Guid.NewGuid(), OrgId = payment.OrgId, CreatedAt = now };
            db.OrgSubscriptions.Add(sub);
        }

        sub.PlanCode           = payment.PlanCode;
        sub.Status             = "active";
        sub.CurrentPeriodStart = now;
        sub.CurrentPeriodEnd   = now.AddDays(365);
        sub.TrialEndsAt        = null;           // paying ends the trial
        sub.Provider           = "razorpay";
        // Reset the reminder stamps so the new period gets its own T-14 / T-3 nudges.
        sub.TrialEndingNotifiedAt = null;
        sub.RenewalT14NotifiedAt  = null;
        sub.RenewalT3NotifiedAt   = null;
        sub.ExpiryNotifiedAt      = null;

        string? promoCode = null;
        if (payment.PromoCodeId is { } promoId)
        {
            var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Id == promoId, ct);
            if (promo is not null)
            {
                promoCode = promo.Code;

                // Redemption first (unique (promo, org) makes it idempotent), then a
                // guarded counter claim. If the counter is already at max we still honor
                // the payment — the buyer paid the price we quoted; overselling by a
                // race is our problem, not theirs.
                var redeemed = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO promo_redemptions (promo_id, org_id, billing_payment_id, is_lifetime)
                    VALUES ({promoId}, {payment.OrgId}, {payment.Id}, {promo.IsLifetime})
                    ON CONFLICT ON CONSTRAINT uq_promo_redemptions_promo_org DO NOTHING", ct);

                if (redeemed > 0)
                {
                    var claimed = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE promo_codes SET redeemed_count = redeemed_count + 1
                        WHERE id = {promoId}
                          AND (max_redemptions IS NULL OR redeemed_count < max_redemptions)", ct);
                    if (claimed == 0)
                        log.LogWarning(
                            "Promo {Code} redeemed past its cap by org {OrgId} — payment honored, cap exceeded by a race.",
                            promo.Code, payment.OrgId);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        planService.Invalidate(payment.OrgId);

        log.LogInformation("Subscription activated for org {OrgId}: {Plan} until {PeriodEnd} (source {Source})",
            payment.OrgId, payment.PlanCode, sub.CurrentPeriodEnd, source);

        await SendReceiptAsync(payment, sub, promoCode, ct);
    }

    public async Task MarkPaymentFailedAsync(
        string razorpayOrderId, string? reason, CancellationToken ct = default)
    {
        var payment = await db.BillingPayments
            .FirstOrDefaultAsync(p => p.RazorpayOrderId == razorpayOrderId, ct);
        // A failure arriving after capture (retry noise) must never undo an activation.
        if (payment is null || payment.Status == "captured") return;

        payment.Status = "failed";
        payment.RawPayload = JsonSerializer.Serialize(new { reason });
        await db.SaveChangesAsync(ct);

        try
        {
            if (payment.CreatedBy is not { } userId) return;
            var member = await audience.ByUserAsync(userId, ct);
            if (member?.Email is null) return;

            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "PAYMENT_FAILED",
                UserId:       userId,
                Payload: new Dictionary<string, object>
                {
                    ["FirstName"]        = FirstName(member.Name),
                    ["OrganizationName"] = await OrgNameAsync(payment.OrgId, ct),
                    ["PlanName"]         = PlanCatalog.Get(payment.PlanCode).Name,
                    ["BillingUrl"]       = BillingUrl,
                },
                Recipients: new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, member.Email),
                    new NotificationRecipientDto(NotificationChannel.InApp, userId.ToString()),
                }), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "PAYMENT_FAILED notification could not be published for order {OrderId}", razorpayOrderId);
        }
    }

    public async Task<IReadOnlyList<BillingPaymentDto>> GetPaymentsAsync(
        Guid orgId, CancellationToken ct = default)
        => await db.BillingPayments.AsNoTracking()
            .Where(p => p.OrgId == orgId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new BillingPaymentDto(
                p.Id, p.PlanCode, p.Cycle, p.AmountPaise, p.OriginalAmountPaise, p.DiscountPaise,
                p.Currency, p.Status, p.RazorpayPaymentId, p.CreatedAt, p.CapturedAt))
            .ToListAsync(ct);

    // ── internals ────────────────────────────────────────────────────────────

    private async Task SendReceiptAsync(
        BillingPayment payment, OrgSubscription sub, string? promoCode, CancellationToken ct)
    {
        try
        {
            if (payment.CreatedBy is not { } userId) return;
            var member = await audience.ByUserAsync(userId, ct);
            if (member?.Email is null) return;

            var payload = new Dictionary<string, object>
            {
                ["FirstName"]        = FirstName(member.Name),
                ["OrganizationName"] = await OrgNameAsync(payment.OrgId, ct),
                ["PlanName"]         = PlanCatalog.Get(payment.PlanCode).Name,
                ["AmountInr"]        = Inr(payment.AmountPaise),
                ["PaymentRef"]       = payment.RazorpayPaymentId ?? payment.Id.ToString(),
                ["PeriodEnd"]        = sub.CurrentPeriodEnd?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "—",
                ["BillingUrl"]       = BillingUrl,
            };
            if (payment.DiscountPaise is > 0 && promoCode is not null)
            {
                payload["DiscountInr"] = Inr(payment.DiscountPaise.Value);
                payload["PromoCode"]   = promoCode;
            }

            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "PAYMENT_RECEIPT",
                UserId:       userId,
                Payload:      payload,
                Recipients: new[]
                {
                    new NotificationRecipientDto(NotificationChannel.Email, member.Email),
                    new NotificationRecipientDto(NotificationChannel.InApp, userId.ToString()),
                }), ct);
        }
        catch (Exception ex)
        {
            // A receipt failure must never fail the activation — they have paid and the
            // subscription is live; the email is recoverable, the money is not.
            log.LogWarning(ex, "PAYMENT_RECEIPT notification could not be published for payment {PaymentId}", payment.Id);
        }
    }

    private async Task<string> OrgNameAsync(Guid orgId, CancellationToken ct)
        => await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your organization";

    private static string FirstName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "there" : name.Split(' ')[0];

    private static string Inr(long paise)
        => (paise / 100m).ToString("N0", CultureInfo.GetCultureInfo("en-IN"));
}
