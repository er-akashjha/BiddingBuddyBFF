using System.Text.Json;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Razorpay webhook receiver — the server-to-server half of activation, so a payment
/// still lands when the buyer closes the tab before the browser callback fires.
///
/// <para>Authentication is an HMAC-SHA256 signature over the RAW body, verified in
/// constant time. Deliberately NOT <c>[PipelineApiKey]</c>: that filter compares with
/// <c>!=</c> and allows the request when its key is unconfigured — acceptable for a
/// pipeline upsert, disqualifying for money. Here an unconfigured secret is a 503.</para>
/// </summary>
[ApiController]
[Route("api/webhooks/razorpay")]
[AllowAnonymous]
[Produces("application/json")]
public class RazorpayWebhookController(
    BffDbContext db,
    IBillingService billing,
    IOptions<RazorpayOptions> options,
    ILogger<RazorpayWebhookController> log) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Fail CLOSED. Accepting unverified webhooks would let anyone who can reach
            // this URL grant themselves a paid subscription.
            log.LogError("Razorpay webhook received but Razorpay:WebhookSecret is not configured — rejecting.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        // Raw body — the signature is over the exact bytes Razorpay sent, so this must be
        // read before any model binding reformats it.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault() ?? string.Empty;
        if (!RazorpaySignature.VerifyWebhook(rawBody, signature, secret))
        {
            log.LogWarning("Razorpay webhook signature mismatch — rejected.");
            return Unauthorized();
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventType = root.TryGetProperty("event", out var evt) ? evt.GetString() ?? "unknown" : "unknown";

        // Razorpay's own delivery id; falls back to the payload id so a missing header
        // still dedups per-payment rather than reprocessing every retry.
        var eventId = Request.Headers["X-Razorpay-Event-Id"].FirstOrDefault()
                      ?? $"{eventType}:{PaymentField(root, "id") ?? Guid.NewGuid().ToString()}";

        var firstDelivery = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO billing_webhook_events (event_id, event_type, payload)
            VALUES ({eventId}, {eventType}, {rawBody}::jsonb)
            ON CONFLICT (event_id) DO NOTHING", ct);

        if (firstDelivery == 0)
        {
            log.LogInformation("Razorpay webhook {EventId} already processed — acknowledging.", eventId);
            return Ok(new { status = "duplicate" });
        }

        var orderId = PaymentField(root, "order_id");
        var paymentId = PaymentField(root, "id");

        switch (eventType)
        {
            case "payment.captured" when orderId is not null && paymentId is not null:
                await billing.ActivateFromPaymentAsync(orderId, paymentId, "webhook", ct);
                break;

            case "payment.failed" when orderId is not null:
                await billing.MarkPaymentFailedAsync(orderId, PaymentField(root, "error_description"), ct);
                break;

            default:
                // Everything else is acknowledged and ignored — an unhandled event type
                // must not make Razorpay retry forever.
                log.LogInformation("Razorpay webhook {EventType} ignored.", eventType);
                break;
        }

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE billing_webhook_events SET processed_at = now() WHERE event_id = {eventId}", ct);

        return Ok(new { status = "ok" });
    }

    /// <summary>Reads payload.payment.entity.{field} — Razorpay's envelope shape.</summary>
    private static string? PaymentField(JsonElement root, string field)
        => root.TryGetProperty("payload", out var payload)
        && payload.TryGetProperty("payment", out var payment)
        && payment.TryGetProperty("entity", out var entity)
        && entity.TryGetProperty(field, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;
}
