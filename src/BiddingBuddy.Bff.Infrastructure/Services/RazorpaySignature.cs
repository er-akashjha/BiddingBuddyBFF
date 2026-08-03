using System.Security.Cryptography;
using System.Text;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Razorpay HMAC-SHA256 signature verification. Both checks use
/// <see cref="CryptographicOperations.FixedTimeEquals"/> — a payment gate must not be a
/// timing oracle. (This is also why <c>PipelineApiKeyAttribute</c> was NOT reused here:
/// its <c>!=</c> compare and allow-when-unconfigured behavior are fine for a pipeline
/// upsert and disqualifying for money.)
/// </summary>
public static class RazorpaySignature
{
    /// <summary>Browser checkout callback: HMAC_SHA256(order_id + "|" + payment_id, KeySecret).</summary>
    public static bool VerifyCheckout(string orderId, string paymentId, string signature, string keySecret)
        => Verify($"{orderId}|{paymentId}", signature, keySecret);

    /// <summary>Webhook: HMAC_SHA256(rawRequestBody, WebhookSecret) vs X-Razorpay-Signature.</summary>
    public static bool VerifyWebhook(string rawBody, string signature, string webhookSecret)
        => Verify(rawBody, signature, webhookSecret);

    private static bool Verify(string payload, string signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret))
            return false;

        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));

        // Razorpay sends lowercase hex.
        byte[] provided;
        try { provided = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
