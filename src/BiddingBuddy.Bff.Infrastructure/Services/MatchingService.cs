using BiddingBuddy.Bff.Core.DTOs.Alerts;
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
    // Cooldown default applied when an org has no org_alert_settings row yet.
    private const int DefaultMinIntervalMinutes = 360;   // 6 h

    // Single-flight guard so the scheduled worker and a manual /internal/matching/scan
    // never run concurrently within this process (which would double-send digests).
    private static readonly SemaphoreSlim ScanGate = new(1, 1);

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
    private string LogoUrl => $"{FrontendBaseUrl}/images/logo/mark-reverse.png";

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

    // ── Scheduled scan: new tenders → per-org digests ──────────────────────────

    public async Task<TenderScanResult> ScanNewTendersAsync(int batchSize, bool rearmFirst = false, CancellationToken ct = default)
    {
        if (batchSize <= 0) batchSize = 200;

        // One scan at a time per process (worker vs. manual trigger vs. itself).
        if (!await ScanGate.WaitAsync(0, ct))
        {
            log.LogInformation("[Match] Scan skipped — another scan is already running.");
            return new TenderScanResult(0, 0, 0, Skipped: true);
        }

        try
        {
            if (rearmFirst)
            {
                var rearmed = await db.Database.ExecuteSqlRawAsync(
                    "UPDATE tenders SET alerts_scanned_at = NULL WHERE alerts_scanned_at IS NOT NULL", ct);
                log.LogWarning("[Match] Backfill requested — re-armed {Count} tender(s) for re-evaluation.", rearmed);
            }

            var rules = await db.TenderAlertRules.Where(r => r.IsActive).ToListAsync(ct);
            var disabledOrgs = (await db.OrgAlertSettings
                .Where(s => !s.IsEnabled).Select(s => s.OrgId).ToListAsync(ct)).ToHashSet();

            int scanned = 0, matchesCreated = 0, orgsNotified = 0;

            while (!ct.IsCancellationRequested)
            {
                // Oldest unscanned tenders first — these are the "newly added" rows.
                var batch = await db.Tenders
                    .Where(t => t.AlertsScannedAt == null)
                    .OrderBy(t => t.CreatedAt)
                    .Take(batchSize)
                    .ToListAsync(ct);
                if (batch.Count == 0) break;

                // Build org → matched tenders, deduped per org across rules.
                var byOrg = new Dictionary<Guid, List<Tender>>();
                var ruleFor = new Dictionary<(Guid Org, Guid Tender), Guid>();

                foreach (var tender in batch)
                {
                    if (!IsLive(tender.Status, tender.ClosingDate)) continue;
                    foreach (var rule in rules)
                    {
                        if (disabledOrgs.Contains(rule.OrgId)) continue;
                        if (ruleFor.ContainsKey((rule.OrgId, tender.Id))) continue;   // org already matched this tender
                        if (!Matches(tender, rule)) continue;

                        ruleFor[(rule.OrgId, tender.Id)] = rule.Id;
                        if (!byOrg.TryGetValue(rule.OrgId, out var list)) { list = []; byOrg[rule.OrgId] = list; }
                        list.Add(tender);
                    }
                }

                // Record match rows (idempotent: skip any already present for the org).
                foreach (var (orgId, matched) in byOrg)
                {
                    var ids = matched.Select(t => t.Id).ToList();
                    var existing = (await db.TenderMatches
                        .Where(m => m.OrgId == orgId && ids.Contains(m.TenderId))
                        .Select(m => m.TenderId).ToListAsync(ct)).ToHashSet();

                    foreach (var t in matched.Where(t => !existing.Contains(t.Id)))
                    {
                        // Buffer as pending — the cooldown-gated flush below groups and
                        // sends them, so we never fire one email per scan tick.
                        db.TenderMatches.Add(new TenderMatch
                        {
                            OrgId    = orgId,
                            TenderId = t.Id,
                            RuleId   = ruleFor[(orgId, t.Id)],
                            Status   = "pending",
                        });
                        matchesCreated++;
                    }
                }

                // Stamp the whole batch scanned (matched or not), then persist.
                foreach (var t in batch) t.AlertsScannedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                scanned += batch.Count;

                if (batch.Count < batchSize) break;   // last (partial) page
            }

            // Flush every org with a pending backlog whose cooldown has elapsed,
            // grouping all its buffered matches into ONE digest. Runs every tick so a
            // backlog buffered on an earlier tick still goes out once its cooldown
            // passes — even on ticks that scanned no new tenders.
            var pendingOrgs = await db.TenderMatches
                .Where(m => m.Status == "pending")
                .Select(m => m.OrgId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var orgId in pendingOrgs)
                if (await FlushOrgAsync(orgId, force: false, ct))
                    orgsNotified++;

            log.LogInformation("[Match] Scan complete — {Scanned} scanned, {Matches} new match(es), {Orgs} org-digest(s) sent.",
                scanned, matchesCreated, orgsNotified);
            return new TenderScanResult(scanned, matchesCreated, orgsNotified, Skipped: false);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    // ── Flush a single org's buffer ────────────────────────────────────────────

    private async Task<bool> FlushOrgAsync(Guid orgId, bool force, CancellationToken ct)
    {
        var settings = await db.OrgAlertSettings.FirstOrDefaultAsync(s => s.OrgId == orgId, ct);
        if (settings is { IsEnabled: false }) return false;

        var interval = TimeSpan.FromMinutes(settings?.MinSendIntervalMinutes ?? DefaultMinIntervalMinutes);

        // Pending matches joined to their tender — ordered for delivery by soonest
        // deadline below.
        var pending = await db.TenderMatches
            .Where(m => m.OrgId == orgId && m.Status == "pending")
            .Join(db.Tenders, m => m.TenderId, t => t.Id, (m, t) => new { Match = m, Tender = t })
            .ToListAsync(ct);
        if (pending.Count == 0) return false;

        // Expire any whose tender deadline has passed / is no longer live.
        var expired = pending.Where(p => !IsLive(p.Tender.Status, p.Tender.ClosingDate)).ToList();
        foreach (var p in expired) { p.Match.Status = "expired"; p.Match.SentAt = DateTime.UtcNow; }

        var live = pending.Except(expired)
            .OrderBy(p => p.Tender.ClosingDate ?? DateOnly.MaxValue)   // soonest-closing first
            .ToList();

        // Cooldown gate: at most one digest per MinSendIntervalMinutes. The first-ever
        // send (no LastDigestSentAt) goes immediately; force=true is the manual
        // /internal/digests/flush drain which ignores the cooldown.
        if (!force && settings?.LastDigestSentAt is { } last && DateTime.UtcNow - last < interval)
        {
            if (expired.Count > 0) await db.SaveChangesAsync(ct);
            return false;
        }
        if (live.Count == 0)
        {
            if (expired.Count > 0) await db.SaveChangesAsync(ct);
            return false;
        }

        // Ensure a settings row exists so the cooldown clock can be stamped (uses the
        // entity's default channels/roles/interval for a brand-new org).
        if (settings is null)
        {
            settings = new OrgAlertSettings { OrgId = orgId };
            db.OrgAlertSettings.Add(settings);
        }

        // Mark this batch sent + stamp the cooldown clock BEFORE dispatching: the
        // publisher shares our DbContext and commits its own transaction, so our
        // change-tracker must be clean first.
        var batchId = Guid.NewGuid();
        foreach (var p in live) { p.Match.Status = "sent"; p.Match.BatchId = batchId; p.Match.SentAt = DateTime.UtcNow; }
        settings.LastDigestSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await DispatchDigestAsync(orgId, live.Select(p => p.Tender).ToList(), settings.NotifyChannels, settings.NotifyRoles, ct);

        log.LogInformation("[Match] Flushed digest for org {OrgId}: {Count} tender(s)", orgId, live.Count);
        return true;
    }

    // ── Dispatch one grouped digest to an org's recipients ─────────────────────
    // Shared by the time-fallback flush and the scheduled scan. Tenders are ordered
    // soonest-closing-first for delivery. Returns true if the org had ≥1 recipient.
    private async Task<bool> DispatchDigestAsync(
        Guid orgId, IReadOnlyList<Tender> tenders, string[] channels, string[] roles, CancellationToken ct)
    {
        if (tenders.Count == 0) return false;

        // UpdateSettingsAsync rejects channels this method can't deliver on, but that guard only
        // covers writes made after it shipped — a row saved before it, or a channel added to
        // TenderDigestChannel.Supported without being wired in below, would otherwise skip this
        // org's digest with no trace. Say so instead of dropping it on the floor.
        var undeliverable = channels.Except(TenderDigestChannel.Supported).ToArray();
        if (undeliverable.Length > 0)
            log.LogWarning(
                "[Match] Org {OrgId} has digest channel(s) [{Channels}] that dispatch does not implement — ignored. Deliverable: [{Supported}]",
                orgId, string.Join(",", undeliverable), string.Join(",", TenderDigestChannel.Supported));

        var recipients = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.Status == "active" && roles.Contains(m.Role))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.Name })
            .Where(u => u.Email != null)
            .Distinct()
            .ToListAsync(ct);

        var ordered = tenders.OrderBy(t => t.ClosingDate ?? DateOnly.MaxValue).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var allUrl = $"{FrontendBaseUrl}/tenders?matched=1";

        var tenderPayload = ordered.Select((t, i) =>
        {
            var daysLeft = t.ClosingDate.HasValue ? t.ClosingDate.Value.DayNumber - today.DayNumber : (int?)null;
            return (object)new Dictionary<string, object?>
            {
                ["Rank"]          = i + 1,
                ["Title"]         = t.Title,
                ["Category"]      = t.Category ?? "",
                ["State"]         = t.State ?? "",
                ["Value"]         = t.TenderValue?.ToString("N0"),
                ["ClosingDate"]   = t.ClosingDate?.ToString("dd MMM yyyy") ?? "—",
                ["DaysLeftLabel"] = DaysLeftLabel(daysLeft),
                ["IsUrgent"]      = daysLeft is >= 0 and <= 7,
                // Link by the Mongo _id (what the SPA /tenders/:id route resolves by),
                // so the link works for tenders from ANY source portal. Fall back to the
                // matched-list page if not yet backfilled — never the wrong tender.
                ["Url"]           = t.MongoTenderId is { Length: > 0 } mid
                                        ? $"{FrontendBaseUrl}/tenders/{mid}"
                                        : allUrl,
            };
        }).ToList();

        var firstTitle = ordered[0].Title;
        var soonestDays = ordered[0].ClosingDate.HasValue
            ? ordered[0].ClosingDate!.Value.DayNumber - today.DayNumber : (int?)null;

        // The InApp/Firebase metadata (migration 0027) renders {{EntityId}} to deep-link the
        // alert. A digest covers N tenders under ONE notification, so it can only name an
        // entity when there is exactly one — otherwise empty, and the clients fall back to
        // the /tenders list. Guid-or-empty because user_notifications.entity_id is uuid and
        // mongo_tender_id is only Guid-shaped for some portals (same contract as the other
        // tender templates in InternalPipelineService).
        var entityId = ordered.Count == 1 && Guid.TryParse(ordered[0].MongoTenderId, out var onlyId)
            ? onlyId.ToString()
            : string.Empty;
        var totalValue = ordered.Sum(t => t.TenderValue ?? 0m);
        var showTotal = ordered.Count > 1 && totalValue > 0;

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
                ["OrgId"]       = orgId.ToString(),    // lets the InApp sender write the inbox row
                ["EntityId"]    = entityId,            // deep-links the inbox row / push tap
                ["FirstName"]   = FirstNameOf(r.Name),
                ["Count"]       = ordered.Count,
                ["One"]         = ordered.Count == 1,
                ["FirstTitle"]  = firstTitle,
                ["Tenders"]     = tenderPayload,
                ["AllUrl"]      = allUrl,
                ["LogoUrl"]     = LogoUrl,
                ["SummaryLine"] = SummaryLine(soonestDays),
                ["ShowTotal"]   = showTotal,
                ["TotalValue"]  = totalValue.ToString("N0"),
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
            log.LogWarning("[Match] Org {OrgId} matched {Count} tenders but has no recipients in roles [{Roles}] — nothing sent",
                orgId, ordered.Count, string.Join(",", roles));

        return recipients.Count > 0;
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

    // Summary-strip line driven by the soonest-closing tender in the digest.
    private static string SummaryLine(int? days) => days switch
    {
        null    => "Newly matched to your interests",
        < 0     => "Some have closed — act now",
        0       => "Soonest closes today",
        1       => "Soonest closes in 1 day",
        _       => $"Soonest closes in {days} days",
    };

    // Per-tender deadline badge label.
    private static string DaysLeftLabel(int? days) => days switch
    {
        null    => "No deadline",
        < 0     => "Closed",
        0       => "Closes today",
        1       => "1 day left",
        _       => $"{days} days left",
    };
}
