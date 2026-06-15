namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One audit row per send attempt. PROCESSOR-OWNED — BFF only reads from this
/// table (e.g. for an admin audit view). Maps to <c>notification_logs</c>.
/// </summary>
public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public Guid NotificationId { get; set; }
    public string Channel { get; set; } = default!;
    public string? Provider { get; set; }
    public string? RecipientAddress { get; set; }
    public string? Subject { get; set; }
    public string Status { get; set; } = default!;         // Sent | Failed (for THIS attempt)
    public string? ProviderMessageId { get; set; }
    public int AttemptNumber { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
