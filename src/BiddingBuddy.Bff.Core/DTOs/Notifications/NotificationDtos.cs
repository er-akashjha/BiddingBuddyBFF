namespace BiddingBuddy.Bff.Core.DTOs.Notifications;

// ── In-app inbox (UserNotification) ──────────────────────────────────────────

public record UserNotificationDto(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    string? EntityType,
    Guid? EntityId,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt
);

public record NotificationPreferenceDto(
    string Channel,
    bool IsEnabled,
    string[] EventTypes
);

public record UpdatePreferencesDto(IReadOnlyList<ChannelPreferenceUpdate> Channels);

public record ChannelPreferenceUpdate(string Channel, bool IsEnabled, string[] EventTypes);

// ── Notification dispatch subsystem ──────────────────────────────────────────

/// <summary>Allowed values for <see cref="Notification.Category"/> (case-sensitive).</summary>
public static class NotificationCategory
{
    public const string Transactional = "Transactional";
    public const string Information   = "Information";
    public const string Marketing     = "Marketing";
}

/// <summary>Allowed channels (case-sensitive — match the CHECK constraints exactly).</summary>
public static class NotificationChannel
{
    public const string Email    = "Email";
    public const string Sms      = "Sms";
    public const string WhatsApp = "WhatsApp";
    public const string Firebase = "Firebase";
    public const string InApp    = "InApp";
}

/// <summary>One channel target inside a SendNotificationDto.</summary>
public record NotificationRecipientDto(
    string Channel,            // Email | Sms | WhatsApp | Firebase | InApp
    string RecipientAddress    // format depends on channel — see handoff
);

/// <summary>
/// Input the publisher consumes to fan out a notification:
/// inserts 1 <c>notifications</c> row + N <c>notification_deliveries</c> rows
/// + publishes N RabbitMQ triggers (one per channel).
/// </summary>
public record SendNotificationDto(
    string Category,                                        // Transactional | Information | Marketing
    string TemplateCode,                                    // e.g. WELCOME
    Guid? UserId,
    IReadOnlyDictionary<string, object> Payload,            // Handlebars model
    IReadOnlyList<NotificationRecipientDto> Recipients
);

/// <summary>Returned by the publisher so callers can correlate / verify.</summary>
public record NotificationDispatchResultDto(
    Guid NotificationId,
    Guid CorrelationId,
    IReadOnlyList<DispatchedDeliveryDto> Deliveries
);

public record DispatchedDeliveryDto(
    Guid DeliveryId,
    string Channel,
    string RecipientAddress,
    bool Published                                          // true if RabbitMQ accepted the trigger
);

// ── Template CRUD DTOs ───────────────────────────────────────────────────────

public record NotificationTemplateDto(
    Guid Id,
    string Code,
    string Channel,
    string Name,
    string? Subject,
    string Body,
    string BodyFormat,
    string Metadata,                // raw JSON string
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateNotificationTemplateDto(
    string Code,
    string Channel,
    string Name,
    string? Subject,
    string Body,
    string BodyFormat = "Html",
    string Metadata   = "{}",
    bool IsActive     = true
);

public record UpdateNotificationTemplateDto(
    string? Name,
    string? Subject,
    string? Body,
    string? BodyFormat,
    string? Metadata,
    bool? IsActive
);
