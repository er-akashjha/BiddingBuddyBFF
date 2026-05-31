namespace BiddingBuddy.Bff.Core.DTOs.Integrations;

public record GemIntegrationDto(
    Guid Id,
    string GemSellerId,
    string? GemUsername,
    bool SyncEnabled,
    DateTime? LastSyncedAt,
    string SyncStatus,
    string? SyncError,
    string? Preferences,
    DateTime UpdatedAt
);

public record UpsertGemIntegrationDto(
    string GemSellerId,
    string? GemUsername,
    bool SyncEnabled,
    string? Preferences
);
