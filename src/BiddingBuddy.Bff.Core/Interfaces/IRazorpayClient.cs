namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>A created Razorpay order — the id is what the browser checkout opens.</summary>
public sealed record RazorpayOrder(string Id, long AmountPaise, string Currency, string Status);

/// <summary>
/// Minimal Razorpay REST surface — order creation only in v1 (one-time annual charges,
/// no subscriptions/mandates). Raw typed HttpClient, no SDK.
/// </summary>
public interface IRazorpayClient
{
    /// <param name="receipt">Our billing_payments row id — ties the order back to us.</param>
    /// <param name="notes">Free-form key/values echoed in webhooks (org_id, plan_code).</param>
    Task<RazorpayOrder> CreateOrderAsync(
        long amountPaise, string currency, string receipt,
        IReadOnlyDictionary<string, string> notes, CancellationToken ct = default);
}
