namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Razorpay webhook dedup: PK on the provider's event id. Insert-on-conflict-do-nothing;
/// a conflict means the event was already handled and the webhook returns 200 untouched.
/// </summary>
public class BillingWebhookEvent
{
    public string EventId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string? Payload { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
