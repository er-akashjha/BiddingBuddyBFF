using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

// Interest matching is no longer triggered inline here — the scheduled
// TenderMatchScanWorker (IMatchingService.ScanNewTendersAsync) picks up newly-added
// tenders via the alerts_scanned_at flag and emails one digest per matched org.
public class InternalPipelineService(
    BffDbContext db,
    IBiddingBuddyServicesClient servicesClient,
    INotificationPublisher notifications,
    INotificationAudienceResolver audience,
    IConfiguration config,
    ILogger<InternalPipelineService> logger) : IInternalPipelineService
{
    private static readonly string[] BidRoles = ["owner", "admin", "bid_manager"];

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<UpsertTenderResponseDto> UpsertTenderAsync(UpsertTenderDto dto, CancellationToken ct = default)
    {
        // Identity is (platform, gem_tender_id) since migration 0022. Older pipelines
        // omit Platform → "gem" (also matches every pre-0021 row via the column default).
        // Case-insensitive to prevent "GeM"/"gem" from creating duplicate rows.
        var platform = string.IsNullOrWhiteSpace(dto.Platform) ? "gem" : dto.Platform.Trim().ToLowerInvariant();

        var existing = await db.Tenders
            .FirstOrDefaultAsync(t => t.Platform == platform && t.GemTenderId == dto.GemTenderId, ct);

        if (existing is null)
        {
            var tender = new Tender
            {
                Platform         = platform,
                GemTenderId      = dto.GemTenderId,
                MongoTenderId    = dto.MongoTenderId,
                Title            = dto.Title,
                Description      = dto.Description,
                BuyerOrgName     = dto.BuyerOrgName,
                BuyerOrgIdGem    = dto.BuyerOrgIdGem,
                State            = dto.State,
                City             = dto.City,
                Category         = dto.Category,
                SubCategory      = dto.SubCategory,
                TenderValue      = dto.TenderValue,
                EmdAmount        = dto.EmdAmount,
                PublishedDate    = dto.PublishedDate,
                ClosingDate      = dto.ClosingDate,
                DeliveryDays     = dto.DeliveryDays,
                Status           = dto.Status ?? "active",
                CorrigendumCount = dto.CorrigendumCount ?? 0,
                AiScore          = dto.AiScore,
                EligibilityScore = dto.EligibilityScore,
                WinProbability   = dto.WinProbability,
                RiskScore        = dto.RiskScore,
                AiSummary        = dto.AiSummary,
                AiTags           = dto.AiTags,
                RawData          = dto.RawData,
            };
            db.Tenders.Add(tender);
            await db.SaveChangesAsync(ct);
            return new UpsertTenderResponseDto(tender.Id, true);
        }

        // Capture the pre-update values an amendment is detected from.
        var prevClosing    = existing.ClosingDate;
        var prevCorrigenda = existing.CorrigendumCount;

        existing.Title            = dto.Title;
        // Set once and keep — the Mongo _id is stable for a tender; never clobber a
        // populated value with a (possibly stale/absent) one from a later upsert.
        existing.MongoTenderId    ??= dto.MongoTenderId;
        existing.Description      = dto.Description ?? existing.Description;
        existing.BuyerOrgName     = dto.BuyerOrgName ?? existing.BuyerOrgName;
        existing.BuyerOrgIdGem    = dto.BuyerOrgIdGem ?? existing.BuyerOrgIdGem;
        existing.State            = dto.State ?? existing.State;
        existing.City             = dto.City ?? existing.City;
        existing.Category         = dto.Category ?? existing.Category;
        existing.SubCategory      = dto.SubCategory ?? existing.SubCategory;
        existing.TenderValue      = dto.TenderValue ?? existing.TenderValue;
        existing.EmdAmount        = dto.EmdAmount ?? existing.EmdAmount;
        existing.PublishedDate    = dto.PublishedDate ?? existing.PublishedDate;
        existing.ClosingDate      = dto.ClosingDate ?? existing.ClosingDate;
        existing.DeliveryDays     = dto.DeliveryDays ?? existing.DeliveryDays;
        existing.Status           = dto.Status ?? existing.Status;
        existing.CorrigendumCount = dto.CorrigendumCount ?? existing.CorrigendumCount;
        existing.AiScore          = dto.AiScore ?? existing.AiScore;
        existing.EligibilityScore = dto.EligibilityScore ?? existing.EligibilityScore;
        existing.WinProbability   = dto.WinProbability ?? existing.WinProbability;
        existing.RiskScore        = dto.RiskScore ?? existing.RiskScore;
        existing.AiSummary        = dto.AiSummary ?? existing.AiSummary;
        existing.AiTags           = dto.AiTags ?? existing.AiTags;
        // Platform is part of the identity — the lookup above matched on it, so any
        // dto.Platform we could assign here would either be a no-op or a case-only
        // diff (both rows normalized to lowercase). Nothing to do.
        if (dto.RawData is not null) existing.RawData = dto.RawData;

        await db.SaveChangesAsync(ct);

        // Amendment = the closing date moved, or the corrigendum count went up. Both are
        // things an org actively bidding on this tender needs to know immediately.
        var closingChanged = existing.ClosingDate.HasValue && prevClosing.HasValue
                             && existing.ClosingDate != prevClosing;
        var corrigendumUp  = existing.CorrigendumCount > prevCorrigenda;
        if (closingChanged || corrigendumUp)
            await NotifyTenderAmendedAsync(existing, closingChanged, ct);

        return new UpsertTenderResponseDto(existing.Id, false);
    }

    /// <summary>
    /// Notify every org with an OPEN bid on this tender that it was amended. One claim per
    /// (tender, org, signature) in notification_reminders dedups repeated upserts of the same
    /// change. Recipients = each org's bid assignees + bid managers. Never throws.
    /// </summary>
    private async Task NotifyTenderAmendedAsync(Tender tender, bool closingChanged, CancellationToken ct)
    {
        try
        {
            var bids = await db.Bids
                .AsNoTracking()
                .Where(b => b.TenderId == tender.Id && b.StatusCategory == "open")
                .Select(b => new { b.OrgId, b.AssignedTo })
                .ToListAsync(ct);
            if (bids.Count == 0) return;

            // Signature so the same amendment isn't re-sent on later upserts.
            var sig = closingChanged
                ? (tender.ClosingDate?.ToString("yyyyMMdd") ?? "nd")
                : $"c{tender.CorrigendumCount}";

            var changeText = closingChanged && tender.ClosingDate.HasValue
                ? $"The closing date is now {tender.ClosingDate.Value:dd MMM yyyy}."
                : "A new corrigendum was published.";

            // Deep-link by the Mongo id (what the SPA /tenders/:id route resolves by).
            Guid? entityId = Guid.TryParse(tender.MongoTenderId, out var mid) ? mid : null;
            var link = entityId is { } e ? $"{FrontendBaseUrl}/tenders/{e}" : $"{FrontendBaseUrl}/tenders";

            foreach (var orgId in bids.Select(b => b.OrgId).Distinct())
            {
                // Per-org dedup: org id in the reminder key + org_id column.
                if (!await TryClaimReminderAsync(orgId, "tender", tender.Id, $"AMENDED:{sig}:{orgId}", ct))
                    continue;

                // Recipients = this org's assignees on the tender + its bid managers.
                var targets = new Dictionary<Guid, NotificationAudienceMember>();
                foreach (var uid in bids.Where(b => b.OrgId == orgId && b.AssignedTo != null)
                                        .Select(b => b.AssignedTo!.Value).Distinct())
                {
                    var m = await audience.ByUserAsync(uid, ct);
                    if (m is not null) targets[m.UserId] = m;
                }
                foreach (var m in await audience.ByRolesAsync(orgId, BidRoles, null, ct))
                    targets[m.UserId] = m;
                if (targets.Count == 0) continue;

                foreach (var m in targets.Values)
                {
                    var recipients = new List<NotificationRecipientDto>
                    {
                        new(NotificationChannel.InApp, m.UserId.ToString()),
                    };
                    if (!string.IsNullOrWhiteSpace(m.Email))
                        recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, m.Email!));

                    await notifications.SendAsync(new SendNotificationDto(
                        Category:     NotificationCategory.Transactional,
                        TemplateCode: "TENDER_AMENDED",
                        UserId:       m.UserId,
                        Payload: new Dictionary<string, object>
                        {
                            ["FirstName"]   = FirstNameOf(m.Name),
                            ["TenderTitle"] = tender.Title,
                            ["ChangeText"]  = changeText,
                            ["OrgId"]       = orgId.ToString(),
                            ["EntityId"]    = entityId?.ToString() ?? string.Empty,
                            ["Link"]        = link,
                        },
                        Recipients: recipients), ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TENDER_AMENDED notification failed for tender {TenderId}", tender.Id);
        }
    }

    private async Task<bool> TryClaimReminderAsync(Guid orgId, string entityType, Guid entityId, string key, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO notification_reminders (org_id, entity_type, entity_id, reminder_key)
            VALUES ({orgId}, {entityType}, {entityId}, {key})
            ON CONFLICT (entity_type, entity_id, reminder_key) DO NOTHING", ct);
        return rows == 1;
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];

    public async Task<BackfillTenderMongoIdResultDto> BackfillTenderMongoIdsAsync(
        int batchSize, CancellationToken ct = default)
    {
        var size = Math.Clamp(batchSize, 1, 1000);

        // Oldest first — these are the rows that predate migration 0010 and are least
        // likely to be re-touched by the pipeline soon, so they benefit most from the push.
        var pending = await db.Tenders
            .Where(t => t.MongoTenderId == null)
            .OrderBy(t => t.CreatedAt)
            .Take(size)
            .ToListAsync(ct);

        int updated = 0, notFound = 0, failed = 0;

        foreach (var tender in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // No direct by-gem-id lookup exists; the free-text search (NameContains)
                // matches the gem reference against name/tag/sourceTenderId on the Services
                // side. Confirm with an EXACT gem-id match so a fuzzy hit can't write the
                // wrong Mongo id onto this row.
                var matches = await servicesClient.SearchTendersAsync(
                    new TenderSearchQueryDto { NameContains = tender.GemTenderId, PageSize = 20 }, ct);

                var match = matches.FirstOrDefault(m =>
                    string.Equals(m.GemTenderId, tender.GemTenderId, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    notFound++;
                    logger.LogWarning("[Backfill] No Mongo tender matched gem id {GemId}", tender.GemTenderId);
                    continue;
                }

                tender.MongoTenderId = match.Id.ToString();

                // Persist per row so one bad row (e.g. a partial-unique-index collision when
                // two stub rows share a gem id) can't abort the whole batch.
                try
                {
                    await db.SaveChangesAsync(ct);
                    updated++;
                }
                catch (DbUpdateException ex)
                {
                    failed++;
                    db.Entry(tender).State = EntityState.Detached; // drop the rejected change
                    logger.LogWarning(ex, "[Backfill] Could not persist mongo id for gem id {GemId}", tender.GemTenderId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex, "[Backfill] Lookup failed for gem id {GemId}", tender.GemTenderId);
            }
        }

        var remaining = await db.Tenders.CountAsync(t => t.MongoTenderId == null, ct);
        logger.LogInformation(
            "[Backfill] mongo_tender_id batch: scanned {Scanned}, updated {Updated}, notFound {NotFound}, failed {Failed}, remaining {Remaining}",
            pending.Count, updated, notFound, failed, remaining);

        return new BackfillTenderMongoIdResultDto(pending.Count, updated, notFound, failed, remaining);
    }

    public async Task UpsertDocumentContentAsync(string gemTenderId, UpsertDocumentContentDto dto, CancellationToken ct = default)
    {
        var tender = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == gemTenderId, ct)
            ?? throw new KeyNotFoundException($"Tender with GEM ID '{gemTenderId}' not found.");

        var existing = await db.TenderDocuments
            .FirstOrDefaultAsync(d => d.TenderId == tender.Id && d.S3Key == dto.S3Key, ct);

        if (existing is null)
        {
            db.TenderDocuments.Add(new TenderDocument
            {
                TenderId      = tender.Id,
                FileName      = dto.FileName,
                S3Key         = dto.S3Key,
                DocumentType  = dto.DocumentType,
                FileSizeKb    = dto.FileSizeKb,
                ExtractedText = dto.ExtractedText,
            });
        }
        else
        {
            existing.FileName      = dto.FileName;
            existing.DocumentType  = dto.DocumentType ?? existing.DocumentType;
            existing.FileSizeKb    = dto.FileSizeKb ?? existing.FileSizeKb;
            existing.ExtractedText = dto.ExtractedText ?? existing.ExtractedText;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertCompetitorAsync(UpsertCompetitorDto dto, CancellationToken ct = default)
    {
        var existing = await db.Competitors
            .FirstOrDefaultAsync(c => c.OrgId == dto.OrgId &&
                (c.GemSellerId == dto.GemSellerId || c.CompanyName == dto.CompanyName), ct);

        if (existing is null)
        {
            db.Competitors.Add(new Competitor
            {
                OrgId            = dto.OrgId,
                CompanyName      = dto.CompanyName,
                GemSellerId      = dto.GemSellerId,
                Tier             = dto.Tier,
                ThreatLevel      = dto.ThreatLevel,
                WinRate          = dto.WinRate,
                TotalContracts   = dto.TotalContracts ?? 0,
                TotalWinValue    = dto.TotalWinValue,
                AvgBidValue      = dto.AvgBidValue,
                ActiveStates     = dto.ActiveStates,
                ActiveCategories = dto.ActiveCategories,
                FirstSeenAt      = dto.FirstSeenAt,
                LastSeenAt       = dto.LastSeenAt,
            });
        }
        else
        {
            if (dto.GemSellerId     is not null) existing.GemSellerId      = dto.GemSellerId;
            if (dto.Tier            is not null) existing.Tier             = dto.Tier;
            if (dto.ThreatLevel     is not null) existing.ThreatLevel      = dto.ThreatLevel;
            if (dto.WinRate.HasValue)            existing.WinRate          = dto.WinRate;
            if (dto.TotalContracts.HasValue)     existing.TotalContracts   = dto.TotalContracts.Value;
            if (dto.TotalWinValue.HasValue)      existing.TotalWinValue    = dto.TotalWinValue;
            if (dto.AvgBidValue.HasValue)        existing.AvgBidValue      = dto.AvgBidValue;
            if (dto.ActiveStates     is not null) existing.ActiveStates    = dto.ActiveStates;
            if (dto.ActiveCategories is not null) existing.ActiveCategories = dto.ActiveCategories;
            if (dto.LastSeenAt.HasValue)         existing.LastSeenAt       = dto.LastSeenAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertAiAnalysisAsync(UpsertAiAnalysisDto dto, CancellationToken ct = default)
    {
        var tender = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == dto.GemTenderId, ct)
            ?? throw new KeyNotFoundException($"Tender with GEM ID '{dto.GemTenderId}' not found.");

        var existing = await db.AiAnalysisResults
            .FirstOrDefaultAsync(a => a.TenderId == tender.Id, ct);

        if (existing is null)
        {
            db.AiAnalysisResults.Add(new AiAnalysisResult
            {
                TenderId              = tender.Id,
                ModelUsed             = dto.ModelUsed,
                EligibilityBreakdown  = dto.EligibilityBreakdown,
                RiskFactors           = dto.RiskFactors,
                WinStrategy           = dto.WinStrategy,
                SuggestedBidRange     = dto.SuggestedBidRange,
                RequiredDocuments     = dto.RequiredDocuments,
                KeyClauses            = dto.KeyClauses,
                RawResponse           = dto.RawResponse,
                GeneratedAt           = DateTime.UtcNow,
            });
        }
        else
        {
            if (dto.ModelUsed            is not null) existing.ModelUsed            = dto.ModelUsed;
            if (dto.EligibilityBreakdown is not null) existing.EligibilityBreakdown = dto.EligibilityBreakdown;
            if (dto.RiskFactors          is not null) existing.RiskFactors          = dto.RiskFactors;
            if (dto.WinStrategy          is not null) existing.WinStrategy          = dto.WinStrategy;
            if (dto.SuggestedBidRange    is not null) existing.SuggestedBidRange    = dto.SuggestedBidRange;
            if (dto.RequiredDocuments    is not null) existing.RequiredDocuments    = dto.RequiredDocuments;
            if (dto.KeyClauses           is not null) existing.KeyClauses           = dto.KeyClauses;
            if (dto.RawResponse          is not null) existing.RawResponse          = dto.RawResponse;
            existing.GeneratedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
