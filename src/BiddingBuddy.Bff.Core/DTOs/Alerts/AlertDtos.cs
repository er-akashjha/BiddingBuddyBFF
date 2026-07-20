using BiddingBuddy.Bff.Core.DTOs.Notifications;

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

// ── Legal values for the OrgAlertSettings arrays ─────────────────────────────
// Both columns are bare text[] with no CHECK, and both are consumed by exact match at
// dispatch time. A value nothing matches doesn't fail — it just yields no recipients and
// the org's digests stop, with no error at save time and nothing in the UI to show it.
// TenderAlertRuleService validates against these lists so that state is unreachable.

/// <summary>
/// The channels a tender digest can actually be delivered on.
/// Deliberately NOT all of <see cref="NotificationChannel"/> — that class lists every channel the
/// notification subsystem knows about (Sms, WhatsApp, Firebase), but MatchingService only builds
/// digest recipients for the two below, so the rest are accepted-then-dropped. This list must
/// mirror the channels <c>MatchingService.DispatchDigestAsync</c> implements: adding one here
/// without wiring it in there re-opens the silent drop this list exists to prevent.
/// </summary>
public static class TenderDigestChannel
{
    public static readonly string[] Supported = [NotificationChannel.Email, NotificationChannel.InApp];
}

/// <summary>
/// Roles a digest can be addressed to — the full set of <c>organization_members.role</c>
/// values. Recipients are resolved with <c>roles.Contains(member.Role)</c>, so an unknown
/// role simply matches no one.
/// </summary>
public static class OrgAlertRole
{
    public static readonly string[] All = ["owner", "admin", "bid_manager", "finance", "sales", "viewer"];
}
