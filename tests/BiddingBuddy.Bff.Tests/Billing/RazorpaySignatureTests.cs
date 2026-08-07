using System.Security.Cryptography;
using System.Text;
using BiddingBuddy.Bff.Infrastructure.Services;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Billing;

/// <summary>
/// Signature verification is the ONLY thing standing between the webhook endpoint and
/// anyone who can reach the URL granting themselves a paid subscription, so the failure
/// modes are pinned explicitly — including that an empty secret never verifies.
/// </summary>
public sealed class RazorpaySignatureTests
{
    private const string KeySecret = "test_key_secret";
    private const string WebhookSecret = "test_webhook_secret";

    private static string Hmac(string payload, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    [Fact]
    public void Checkout_signature_over_order_pipe_payment_verifies()
    {
        var signature = Hmac("order_abc|pay_xyz", KeySecret);

        Assert.True(RazorpaySignature.VerifyCheckout("order_abc", "pay_xyz", signature, KeySecret));
    }

    [Fact]
    public void Checkout_signature_from_a_different_payment_is_rejected()
    {
        var signature = Hmac("order_abc|pay_xyz", KeySecret);

        // Same order, a payment id someone else's transaction produced.
        Assert.False(RazorpaySignature.VerifyCheckout("order_abc", "pay_OTHER", signature, KeySecret));
        Assert.False(RazorpaySignature.VerifyCheckout("order_OTHER", "pay_xyz", signature, KeySecret));
    }

    [Fact]
    public void Checkout_signature_under_the_wrong_secret_is_rejected()
    {
        var signature = Hmac("order_abc|pay_xyz", "attacker_guess");

        Assert.False(RazorpaySignature.VerifyCheckout("order_abc", "pay_xyz", signature, KeySecret));
    }

    [Fact]
    public void Webhook_signature_over_the_raw_body_verifies()
    {
        var body = """{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_1"}}}}""";

        Assert.True(RazorpaySignature.VerifyWebhook(body, Hmac(body, WebhookSecret), WebhookSecret));
    }

    [Fact]
    public void A_tampered_webhook_body_is_rejected()
    {
        var body = """{"event":"payment.captured","payload":{"payment":{"entity":{"id":"pay_1"}}}}""";
        var signature = Hmac(body, WebhookSecret);

        var tampered = body.Replace("pay_1", "pay_2");
        Assert.False(RazorpaySignature.VerifyWebhook(tampered, signature, WebhookSecret));
    }

    /// <summary>Fail closed: an unconfigured secret must never mean "everything verifies".</summary>
    [Fact]
    public void Empty_secrets_and_empty_signatures_never_verify()
    {
        var body = """{"event":"payment.captured"}""";

        Assert.False(RazorpaySignature.VerifyWebhook(body, Hmac(body, ""), ""));
        Assert.False(RazorpaySignature.VerifyWebhook(body, "", WebhookSecret));
        Assert.False(RazorpaySignature.VerifyCheckout("o", "p", "", KeySecret));
    }

    /// <summary>A non-hex signature must be rejected, not throw out of the verifier.</summary>
    [Fact]
    public void A_malformed_signature_is_rejected_without_throwing()
    {
        Assert.False(RazorpaySignature.VerifyWebhook("{}", "not-hex-at-all!!", WebhookSecret));
        Assert.False(RazorpaySignature.VerifyCheckout("o", "p", "zzzz", KeySecret));
    }
}
