namespace BiddingBuddy.Bff.Core.DTOs.Billing;

// ── Public pricing page ──────────────────────────────────────────────────────

public record PublicPlanDto(
    string Code,
    string Name,
    long PricePaiseAnnual,
    long AnchorPricePaiseAnnual,
    int SeatCap,
    int? AiSummariesPerMonth,
    int? SavedFilterCap,
    int AlertFloorMinutes,
    IReadOnlyList<string> Features,
    bool IsPopular,
    string Tagline,
    IReadOnlyList<string> Bullets);

public record PublicPlansDto(IReadOnlyList<PublicPlanDto> Plans, bool CheckoutEnabled);

// ── Billing summary ──────────────────────────────────────────────────────────

/// <summary>A lifetime promo discount the org has already earned; auto-applied at renewal.</summary>
public record LifetimeDiscountDto(string Code, string DiscountType, long DiscountValue);

public record BillingSummaryDto(
    string PlanCode,
    string PlanName,
    string Status,
    DateTime? TrialEndsAt,
    DateTime? PeriodEnd,
    int SeatsUsed,
    int SeatCap,
    int AiUsed,
    int? AiQuota,
    bool CheckoutEnabled,
    LifetimeDiscountDto? LifetimeDiscount);

// ── Promo validation ─────────────────────────────────────────────────────────

public record ValidatePromoDto(string Code, string PlanCode);

/// <summary>
/// 200 with <c>Valid=false</c> + <c>Error</c> (INVALID_CODE | EXPIRED | EXHAUSTED |
/// NOT_APPLICABLE | ALREADY_USED) rather than a 4xx — a wrong code is expected input,
/// not an exceptional path. All money figures computed server-side.
/// </summary>
public record PromoValidationDto(
    bool Valid,
    string? Error,
    string? Code = null,
    string? Description = null,
    string? DiscountType = null,
    long? DiscountValue = null,
    bool IsLifetime = false,
    long OriginalAmountPaise = 0,
    long DiscountPaise = 0,
    long FinalAmountPaise = 0);

// ── Checkout ─────────────────────────────────────────────────────────────────

public record CreateCheckoutDto(string PlanCode, string Cycle = "annual", string? PromoCode = null);

public record CheckoutOrderDto(
    Guid PaymentId,
    string RazorpayOrderId,
    long AmountPaise,
    long? OriginalAmountPaise,
    long? DiscountPaise,
    string? PromoCode,
    string Currency,
    string KeyId,
    string PlanName,
    string OrgName);

public record VerifyPaymentDto(string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);

// ── Payment history ──────────────────────────────────────────────────────────

public record BillingPaymentDto(
    Guid Id,
    string PlanCode,
    string Cycle,
    long AmountPaise,
    long? OriginalAmountPaise,
    long? DiscountPaise,
    string Currency,
    string Status,
    string? RazorpayPaymentId,
    DateTime CreatedAt,
    DateTime? CapturedAt);

// ── Internal promo admin (/internal/promo-codes, X-Api-Key) ─────────────────

public record CreatePromoCodeDto(
    string Code,
    string? Description,
    string DiscountType,
    long DiscountValue,
    string[]? AppliesToPlans = null,
    int? MaxRedemptions = null,
    bool IsLifetime = false,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    bool IsActive = true);

public record UpdatePromoCodeDto(
    bool? IsActive = null,
    DateTime? ValidUntil = null,
    int? MaxRedemptions = null,
    string? Description = null);

public record PromoCodeAdminDto(
    Guid Id,
    string Code,
    string? Description,
    string DiscountType,
    long DiscountValue,
    string[]? AppliesToPlans,
    int? MaxRedemptions,
    int RedeemedCount,
    bool IsLifetime,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    bool IsActive,
    DateTime CreatedAt);
