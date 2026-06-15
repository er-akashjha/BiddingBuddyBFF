namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One logical notification event with its shared payload + category.
/// Fan-out happens via the child <see cref="NotificationDelivery"/> rows
/// (one per channel). Maps to <c>notifications</c>.
///
/// Note: this REPLACES the old in-app inbox entity, which is now
/// <see cref="UserNotification"/> → table <c>user_notifications</c>.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }
    public string Category { get; set; } = default!;       // Transactional | Information | Marketing
    public string TemplateCode { get; set; } = default!;   // e.g. WELCOME, TEAM_INVITATION
    public Guid? UserId { get; set; }                      // optional — recipient hint for the template
    public string Payload { get; set; } = "{}";            // jsonb — Handlebars model
    public Guid CorrelationId { get; set; }                // server-generated; forwarded into RabbitMQ msgs
    public DateTime CreatedAt { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}
