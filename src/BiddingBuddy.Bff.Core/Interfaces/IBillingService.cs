using BiddingBuddy.Bff.Core.DTOs.Billing;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IBillingService
{
    Task<BillingSummaryDto> GetSummaryAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>
    /// Create a billing_payments row + Razorpay order at the SERVER-computed price
    /// (catalog price minus promo/lifetime discount — the client never sends an amount).
    /// Throws InvalidOperationException("CHECKOUT_UNAVAILABLE") when Razorpay is unconfigured.
    /// </summary>
    Task<CheckoutOrderDto> CreateCheckoutAsync(
        Guid orgId, Guid userId, CreateCheckoutDto dto, CancellationToken ct = default);

    /// <summary>Browser handler callback: verify the checkout signature, then activate.</summary>
    Task<BillingSummaryDto> VerifyAndActivateAsync(
        Guid orgId, VerifyPaymentDto dto, CancellationToken ct = default);

    /// <summary>
    /// The idempotent activation core shared by the verify callback and the webhook —
    /// whichever arrives second is a no-op (row lock on billing_payments by order id).
    /// </summary>
    Task ActivateFromPaymentAsync(
        string razorpayOrderId, string razorpayPaymentId, string source, CancellationToken ct = default);

    Task MarkPaymentFailedAsync(string razorpayOrderId, string? reason, CancellationToken ct = default);

    Task<IReadOnlyList<BillingPaymentDto>> GetPaymentsAsync(Guid orgId, CancellationToken ct = default);
}
