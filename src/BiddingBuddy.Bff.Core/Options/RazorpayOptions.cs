namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Razorpay payment configuration. KeyId is public (it ships to the browser checkout);
/// KeySecret and WebhookSecret must live in user-secrets (dev) or the deployed secrets
/// store (prod) — never committed.
///
/// The whole billing surface degrades gracefully while unconfigured: checkout returns
/// 503 CHECKOUT_UNAVAILABLE and the UI shows "Contact us" — a Razorpay account is a
/// go-live prerequisite (like DNS/OAuth), not a build dependency.
/// </summary>
public sealed class RazorpayOptions
{
    public const string Section = "Razorpay";

    /// <summary>Publishable key id (rzp_test_… / rzp_live_…).</summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>API secret — Basic-auth pair for order creation. Secret store only.</summary>
    public string KeySecret { get; init; } = string.Empty;

    /// <summary>HMAC secret configured on the Razorpay webhook. Secret store only.</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(KeySecret);
}
