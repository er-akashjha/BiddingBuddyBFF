namespace BiddingBuddy.Bff.Core.DTOs.Alerts;

// ── Tender alert rules (client "interests") ──────────────────────────────────

public record TenderAlertRuleDto(
    Guid Id,
    string Name,
    string[]? Categories,
    string[]? States,
    string[]? Keywords,
    decimal? MinValue,
    decimal? MaxValue,
    int? MinAiScore,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateTenderAlertRuleDto(
    string Name,
    string[]? Categories,
    string[]? States,
    string[]? Keywords,
    decimal? MinValue,
    decimal? MaxValue,
    int? MinAiScore,
    bool IsActive = true
);

public record UpdateTenderAlertRuleDto(
    string? Name,
    string[]? Categories,
    string[]? States,
    string[]? Keywords,
    decimal? MinValue,
    decimal? MaxValue,
    int? MinAiScore,
    bool? IsActive
);

// ── Per-org digest delivery settings ─────────────────────────────────────────

public record OrgAlertSettingsDto(
    bool IsEnabled,
    int DigestSize,
    int MinSendIntervalMinutes,
    string[] NotifyChannels,
    string[] NotifyRoles
);

public record UpdateOrgAlertSettingsDto(
    bool? IsEnabled,
    int? DigestSize,
    int? MinSendIntervalMinutes,
    string[]? NotifyChannels,
    string[]? NotifyRoles
);
