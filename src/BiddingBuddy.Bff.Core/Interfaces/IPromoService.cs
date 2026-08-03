using BiddingBuddy.Bff.Core.DTOs.Billing;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>A promo resolved for a specific checkout: who to credit and how much off.</summary>
public sealed record PromoApplication(Guid PromoId, string Code, bool IsLifetime, long DiscountPaise);

public interface IPromoService
{
    /// <summary>Preview a code against a plan. Never throws for a bad code — see PromoValidationDto.</summary>
    Task<PromoValidationDto> ValidateAsync(Guid orgId, string code, string planCode, CancellationToken ct = default);

    /// <summary>
    /// Resolve the discount for a checkout: an explicitly entered code (throws
    /// InvalidOperationException with the validation error code when unusable — checkout
    /// must not silently drop a discount the buyer thinks is applied), else the org's
    /// earned lifetime discount, else null.
    /// </summary>
    Task<PromoApplication?> ResolveForCheckoutAsync(
        Guid orgId, string? code, string planCode, long amountPaise, CancellationToken ct = default);

    /// <summary>The org's best already-earned lifetime discount, for summary display + renewal auto-apply.</summary>
    Task<LifetimeDiscountDto?> GetActiveLifetimeDiscountAsync(Guid orgId, CancellationToken ct = default);
}
