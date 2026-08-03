namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One row per checkout attempt. `RazorpayOrderId` is unique — the verify callback and
/// the webhook both activate through a FOR UPDATE on this row (race/replay safety).
/// Discount fields are a snapshot so history stays truthful if the promo code changes.
/// </summary>
public class BillingPayment
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string PlanCode { get; set; } = default!;
    public string Cycle { get; set; } = "annual";
    /// <summary>What Razorpay was asked to charge (post-discount), in paise.</summary>
    public long AmountPaise { get; set; }
    public string Currency { get; set; } = "INR";

    public Guid? PromoCodeId { get; set; }
    public long? OriginalAmountPaise { get; set; }
    public long? DiscountPaise { get; set; }

    public string? RazorpayOrderId { get; set; }
    public string? RazorpayPaymentId { get; set; }
    public string Status { get; set; } = "created";  // created|captured|failed
    public bool SignatureVerified { get; set; }
    public string? RawPayload { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CapturedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
