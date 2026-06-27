using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
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
    INotificationPublisher publisher,
    IConfiguration config,
    ILogger<InternalPipelineService> log) : IInternalPipelineService
{
    // Roles that receive the "AI analysis ready" notification — mirrors the digest defaults.
    private static readonly string[] NotifyRoles = ["owner", "admin", "bid_manager"];
    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<UpsertTenderResponseDto> UpsertTenderAsync(UpsertTenderDto dto, CancellationToken ct = default)
    {
        var existing = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == dto.GemTenderId, ct);

        Guid tenderId;
        bool created;
        Tender entity;
        // Did this upsert move the tender INTO the enriched state? Used to unlock every
        // org that paid-and-queued. Computed before we mutate existing.EnrichmentStatus.
        bool becameEnriched;

        if (existing is null)
        {
            var tender = new Tender
            {
                GemTenderId      = dto.GemTenderId,
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
                EnrichmentStatus = dto.EnrichmentStatus ?? "none",
            };
            db.Tenders.Add(tender);
            await db.SaveChangesAsync(ct);

            becameEnriched = dto.EnrichmentStatus == "enriched";
            tenderId = tender.Id;
            created  = true;
            entity   = tender;
        }
        else
        {
            existing.Title            = dto.Title;
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
            if (dto.RawData is not null) existing.RawData = dto.RawData;

            becameEnriched = dto.EnrichmentStatus == "enriched" && existing.EnrichmentStatus != "enriched";

            // Apply the new status, but never let a raw re-projection ("extracted")
            // downgrade an already-enriched tender.
            if (dto.EnrichmentStatus is not null &&
                !(existing.EnrichmentStatus == "enriched" && dto.EnrichmentStatus == "extracted"))
            {
                existing.EnrichmentStatus = dto.EnrichmentStatus;
            }

            await db.SaveChangesAsync(ct);

            tenderId = existing.Id;
            created  = false;
            entity   = existing;
        }

        // Enrichment just completed → unlock every org that paid-and-queued for this
        // tender, then email them. Idempotent (only flips 'pending' rows / sends once via
        // enrichment_notified_at), so a repeated enriched mirror is a no-op the second time.
        if (becameEnriched)
        {
            await db.TenderEnrichmentEntitlements
                .Where(e => e.GemTenderId == dto.GemTenderId && e.Status == "pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, "unlocked")
                    .SetProperty(e => e.UnlockedAt, DateTime.UtcNow), ct);

            await SendEnrichmentReadyAsync(entity, ct);
        }

        return new UpsertTenderResponseDto(tenderId, created);
    }

    /// <summary>
    /// Emails "your AI analysis is ready" to every org that funded this tender's
    /// enrichment. No-op when no org paid (e.g. an auto-enriched tender during rollout)
    /// or when already sent. Best-effort: a notification failure never fails the upsert.
    /// </summary>
    private async Task SendEnrichmentReadyAsync(Tender tender, CancellationToken ct)
    {
        // Idempotency: send at most once per tender.
        if (tender.EnrichmentNotifiedAt is not null) return;

        var orgIds = await db.TenderEnrichmentEntitlements
            .Where(e => e.GemTenderId == tender.GemTenderId)
            .Select(e => e.OrgId)
            .Distinct()
            .ToListAsync(ct);

        if (orgIds.Count > 0)
        {
            var url = $"{FrontendBaseUrl}/tenders/{tender.Id}";

            foreach (var orgId in orgIds)
            {
                var recipients = await db.OrgMembers
                    .Where(m => m.OrgId == orgId && m.Status == "active" && NotifyRoles.Contains(m.Role))
                    .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, u.Name })
                    .Where(u => u.Email != null)
                    .Distinct()
                    .ToListAsync(ct);

                foreach (var r in recipients)
                {
                    var payload = new Dictionary<string, object>
                    {
                        ["FirstName"] = FirstNameOf(r.Name),
                        ["Title"]     = tender.Title,
                        ["Category"]  = tender.Category ?? "",
                        ["Url"]       = url,
                    };

                    try
                    {
                        await publisher.SendAsync(new SendNotificationDto(
                            Category:     NotificationCategory.Information,
                            TemplateCode: "TENDER_ENRICHMENT_READY",
                            UserId:       r.Id,
                            Payload:      payload,
                            Recipients: new[]
                            {
                                new NotificationRecipientDto(NotificationChannel.Email, r.Email!),
                                new NotificationRecipientDto(NotificationChannel.InApp, r.Id.ToString()),
                            }), ct);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex,
                            "[Enrich] Failed to send AI-ready notification to user {UserId} (org {OrgId})", r.Id, orgId);
                    }
                }
            }
        }

        // Stamp notified even when there were no funding orgs/recipients, so we don't
        // re-scan on every subsequent enriched mirror for this tender.
        tender.EnrichmentNotifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];

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
