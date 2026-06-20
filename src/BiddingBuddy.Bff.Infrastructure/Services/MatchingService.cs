using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class MatchingService(
    BffDbContext db,
    INotificationPublisher publisher,
    IConfiguration config,
    ILogger<MatchingService> log) : IMatchingService
{
    // Defaults applied when an org has no org_alert_settings row yet.
    private static readonly string[] DefaultChannels = ["Email", "InApp"];
    private static readonly string[] DefaultRoles = ["owner", "admin", "bid_manager"];
    private const int DefaultDigestSize = 10;

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task OnTenderUpsertedAsync(Guid tenderId, CancellationToken ct = default)
    {
        var tender = await db.Tenders.FirstOrDefaultAsync(t => t.Id == tenderId, ct);
        if (tender is null || !IsLive(tender.Status, tender.ClosingDate))
            return;

        var rules = await db.TenderAlertRules.Where(r => r.IsActive).ToListAsync(ct);
        if (rules.Count == 0) return;

        // org_id -> the first rule that matched (for attribution)
        var matchedByOrg = new Dictionary<Guid, Guid>();
        foreach (var rule in rules)
        {
            if (!matchedByOrg.ContainsKey(rule.OrgId) && Matches(tender, rule))
                matchedByOrg[rule.OrgId] = rule.Id;
        }
        if (matchedByOrg.Count == 0) return;

        // Skip orgs that have disabled alerts.
        var orgIds = matchedByOrg.Keys.ToList();
        var disabled = await db.OrgAlertSettings
            .Where(s => orgIds.Contains(s.OrgId) && !s.IsEnabled)
            .Select(s => s.OrgId)
            .ToListAsync(ct);
        foreach (var id in disabled) matchedByOrg.Remove(id);
        if (matchedByOrg.Count == 0) return;

        // Dedup: only insert a buffer row where one doesn't already exist for (org, tender).
        var targetOrgs = matchedByOrg.Keys.ToList();
        var existing = await db.TenderMatches
            .Where(m => m.TenderId == tenderId && targetOrgs.Contains(m.OrgId))
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        var fresh = targetOrgs.Except(existing).ToList();
        foreach (var orgId in fresh)
        {
            db.TenderMatches.Add(new TenderMatch
            {
                OrgId    = orgId,
                TenderId = tenderId,
                RuleId   = matchedByOrg[orgId],
                Status   = "pending",
            });
        }
        if (fresh.Count > 0) await db.SaveChangesAsync(ct);

        // Count-triggered flush for each newly-affected org.
        foreach (var orgId in fresh)
            await FlushOrgAsync(orgId, force: false, ct);
    }

    public async Task<int> FlushAllDueAsync(CancellationToken ct = default)
    {
        var orgIds = await db.TenderMatches
            .Where(m => m.Status == "pending")
            .Select(m => m.OrgId)
            .Distinct()
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var orgId in orgIds)
            if (await FlushOrgAsync(orgId, force: true, ct))
                delivered++;
        return delivered;
    }

    // ── Flush a single org's buffer ────────────────────────────────────────────

    private async Task<bool> FlushOrgAsync(Guid orgId, bool force, CancellationToken ct)
    {
        var settings = await db.OrgAlertSettings.FirstOrDefaultAsync(s => s.OrgId == orgId, ct);
        if (settings is { IsEnabled: false }) return false;

        var digestSize = settings?.DigestSize ?? DefaultDigestSize;
        var channels   = settings?.NotifyChannels ?? DefaultChannels;
        var roles      = settings?.NotifyRoles ?? DefaultRoles;

        // Pending matches joined to their tender, newest-matched irrelevant — we
        // order for delivery by soonest deadline below.
        var pending = await db.TenderMatches
            .Where(m => m.OrgId == orgId && m.Status == "pending")
            .Join(db.Tenders, m => m.TenderId, t => t.Id, (m, t) => new { Match = m, Tender = t })
            .ToListAsync(ct);
        if (pending.Count == 0) return false;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Expire any whose tender deadline has passed / is no longer live.
        var expired = pending.Where(p => !IsLive(p.Tender.Status, p.Tender.ClosingDate)).ToList();
        foreach (var p in expired) { p.Match.Status = "expired"; p.Match.SentAt = DateTime.UtcNow; }

        var live = pending.Except(expired)
            .OrderBy(p => p.Tender.ClosingDate ?? DateOnly.MaxValue)   // soonest-closing first
            .ToList();

        // Count trigger: hold until the batch fills (unless this is the time-fallback).
        if (!force && live.Count < digestSize)
        {
            if (expired.Count > 0) await db.SaveChangesAsync(ct);
            return false;
        }
        if (live.Count == 0)
        {
            if (expired.Count > 0) await db.SaveChangesAsync(ct);
            return false;
        }

        var recipients = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.Status == "active" && roles.Contains(m.Role))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.Name })
            .Where(u => u.Email != null)
            .Distinct()
            .ToListAsync(ct);

        var batchId = Guid.NewGuid();
        var tenders = live.Select(p => (object)new Dictionary<string, object?>
        {
            ["Title"]       = p.Tender.Title,
            ["Category"]    = p.Tender.Category ?? "",
            ["State"]       = p.Tender.State ?? "",
            ["Value"]       = p.Tender.TenderValue?.ToString("N0"),
            ["ClosingDate"] = p.Tender.ClosingDate?.ToString("dd MMM yyyy") ?? "—",
            ["Url"]         = $"{FrontendBaseUrl}/tenders/{p.Tender.Id}",
        }).ToList();

        var firstTitle = live[0].Tender.Title;
        var allUrl = $"{FrontendBaseUrl}/tenders?matched=1";

        foreach (var r in recipients)
        {
            var channelRecipients = new List<NotificationRecipientDto>();
            if (channels.Contains(NotificationChannel.Email))
                channelRecipients.Add(new NotificationRecipientDto(NotificationChannel.Email, r.Email!));
            if (channels.Contains(NotificationChannel.InApp))
                channelRecipients.Add(new NotificationRecipientDto(NotificationChannel.InApp, r.Id.ToString()));
            if (channelRecipients.Count == 0) continue;

            var payload = new Dictionary<string, object>
            {
                ["FirstName"]  = FirstNameOf(r.Name),
                ["Count"]      = live.Count,
                ["One"]        = live.Count == 1,
                ["FirstTitle"] = firstTitle,
                ["Tenders"]    = tenders,
                ["AllUrl"]     = allUrl,
            };

            try
            {
                await publisher.SendAsync(new SendNotificationDto(
                    Category:     NotificationCategory.Information,   // opportunity alerts, not transactional
                    TemplateCode: "TENDER_MATCH",
                    UserId:       r.Id,
                    Payload:      payload,
                    Recipients:   channelRecipients), ct);
            }
            catch (Exception ex)
            {
                // A single recipient's dispatch failure must not strand the whole batch.
                log.LogWarning(ex, "[Match] Failed to dispatch digest to user {UserId} (org {OrgId})", r.Id, orgId);
            }
        }

        if (recipients.Count == 0)
            log.LogWarning("[Match] Org {OrgId} has {Count} matched tenders but no recipients in roles [{Roles}] — marking sent",
                orgId, live.Count, string.Join(",", roles));

        foreach (var p in live) { p.Match.Status = "sent"; p.Match.BatchId = batchId; p.Match.SentAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);

        log.LogInformation("[Match] Flushed digest for org {OrgId}: {Count} tenders to {Recipients} recipient(s)",
            orgId, live.Count, recipients.Count);
        return true;
    }

    // ── Predicate ──────────────────────────────────────────────────────────────

    private static bool Matches(Tender t, TenderAlertRule r)
    {
        if (r.Categories is { Length: > 0 } cats &&
            !(EqualsAny(t.Category, cats) || EqualsAny(t.SubCategory, cats)))
            return false;

        if (r.States is { Length: > 0 } states && !EqualsAny(t.State, states))
            return false;

        if (r.MinValue is { } min && (t.TenderValue is null || t.TenderValue < min))
            return false;

        if (r.MaxValue is { } max && (t.TenderValue is null || t.TenderValue > max))
            return false;

        if (r.MinAiScore is { } minScore && (t.AiScore is null || t.AiScore < minScore))
            return false;

        if (r.Keywords is { Length: > 0 } keywords)
        {
            var hay = string.Join(' ', new[] { t.Title, t.Description, t.AiSummary }
                .Concat(t.AiTags ?? []).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!keywords.Any(k => hay.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static bool EqualsAny(string? value, string[] options) =>
        value is not null && options.Any(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsLive(string status, DateOnly? closing)
    {
        var live = status.Equals("active", StringComparison.OrdinalIgnoreCase)
                || status.Equals("open", StringComparison.OrdinalIgnoreCase);
        return live && (closing is null || closing >= DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];
}
