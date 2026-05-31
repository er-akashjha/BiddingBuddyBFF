namespace BiddingBuddy.Bff.Core.DTOs.Notifications;

public record NotificationDto(
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
