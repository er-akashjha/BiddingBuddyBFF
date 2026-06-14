namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One per-channel processing unit for a notification.
/// BFF inserts in Pending state then publishes a thin trigger to RabbitMQ; the
/// processor owns every column after insert (status, retry, locks, errors).
/// Maps to <c>notification_deliveries</c>.
/// </summary>
public class NotificationDelivery
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public string Channel { get; set; } = default!;        // Email | Sms | WhatsApp | Firebase | InApp
    public string RecipientAddress { get; set; } = default!; // email / E.164 phone / FCM token / user-id
    public string Status { get; set; } = "Pending";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 5;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? FailedAt { get; set; }

    public Notification Notification { get; set; } = default!;
}
