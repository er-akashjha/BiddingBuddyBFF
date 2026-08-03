namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Promo/discount code. Managed via /internal/promo-codes only. All discount math is
/// server-side at checkout — the client never computes a price.
/// </summary>
public class PromoCode
{
    public Guid Id { get; set; }
    /// <summary>Stored uppercase; lookups uppercase the input.</summary>
    public string Code { get; set; } = default!;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = default!;  // percent|flat
    /// <summary>percent: 1–100 · flat: paise off the order.</summary>
    public long DiscountValue { get; set; }
    /// <summary>Null = valid for every paid plan.</summary>
    public string[]? AppliesToPlans { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedeemedCount { get; set; }
    /// <summary>Lifetime codes re-apply automatically on every renewal checkout.</summary>
    public bool IsLifetime { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
