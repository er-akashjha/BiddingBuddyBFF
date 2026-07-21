using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Ingests enriched grants from the pipeline into the Postgres shadow index.
/// The grant-line counterpart of <c>InternalPipelineService.UpsertTenderAsync</c>.
/// </summary>
public class InternalGrantPipelineService(
    BffDbContext db,
    ILogger<InternalGrantPipelineService> logger) : IInternalGrantPipelineService
{
    public async Task<UpsertGrantResponseDto> UpsertGrantAsync(UpsertGrantDto dto, CancellationToken ct)
    {
        // Normalise the discriminator the same way the tender path does, so "Grants-Gov" and
        // "grants-gov" cannot become two rows for one opportunity.
        var platform = string.IsNullOrWhiteSpace(dto.Platform)
            ? "grants-gov"
            : dto.Platform.Trim().ToLowerInvariant();

        var existing = await db.GrantOpportunities
            .FirstOrDefaultAsync(g => g.Platform == platform && g.PlatformGrantId == dto.PlatformGrantId, ct);

        if (existing is null)
        {
            var grant = new GrantOpportunity
            {
                Platform          = platform,
                PlatformGrantId   = dto.PlatformGrantId,
                MongoGrantId      = dto.MongoGrantId,
                OpportunityNumber = dto.OpportunityNumber,
                SourceUrl         = dto.SourceUrl,
                Title             = FallbackTitle(dto),
                Summary           = dto.Summary,
                AgencyName        = dto.AgencyName,
                AgencyCode        = dto.AgencyCode,
                Category          = dto.Category,
                Currency          = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency,
                AwardCeiling      = dto.AwardCeiling,
                AwardFloor        = dto.AwardFloor,
                EstimatedTotalProgramFunding = dto.EstimatedTotalProgramFunding,
                ExpectedNumberOfAwards       = dto.ExpectedNumberOfAwards,
                CostSharingRequired          = dto.CostSharingRequired,
                PostedDate           = dto.PostedDate,
                CloseDate            = dto.CloseDate,
                LoiDueDate           = dto.LoiDueDate,
                ArchiveDate          = dto.ArchiveDate,
                CloseDateExplanation = dto.CloseDateExplanation,
                IsRolling            = dto.IsRolling,
                ApplicantTypesRaw    = dto.ApplicantTypesRaw,
                ApplicantTypeCodes   = dto.ApplicantTypeCodes,
                TribalGovernmentsEligible   = dto.TribalGovernmentsEligible,
                TribalOrganizationsEligible = dto.TribalOrganizationsEligible,
                Nonprofit501C3Eligible      = dto.Nonprofit501C3Eligible,
                IsTribalSetAside            = dto.IsTribalSetAside,
                NativeLedPriority           = dto.NativeLedPriority,
                AssistanceListingNumbers    = dto.AssistanceListingNumbers,
                FundingInstruments          = dto.FundingInstruments,
                IsForecast = dto.IsForecast,
                Status     = NormalizeStatus(dto.Status),
                AiScore    = dto.AiScore,
                AiSummary  = dto.AiSummary,
                AiTags     = dto.AiTags,
                CreatedAt  = DateTime.UtcNow,
            };

            db.GrantOpportunities.Add(grant);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("[InternalGrant] Created {Platform}/{GrantId} → {Id}",
                platform, dto.PlatformGrantId, grant.Id);

            return new UpsertGrantResponseDto(grant.Id, true);
        }

        // ── Update: coalescing, so a partial re-scrape cannot blank a populated column ────────
        // `x = dto.x ?? existing.x` throughout. A seed-only re-scrape legitimately arrives with
        // most fields null, and overwriting with those nulls would strip a good record down to
        // its identity — the AI clobber-guard in Mongo exists for exactly this reason and the
        // shadow index must not undo it.
        existing.Title = FallbackTitle(dto);          // always refreshed; never allowed to go blank

        // Set-once. Once the SPA can deep-link by this id, changing it orphans every link and
        // every notification_reminders row already pointing at it.
        existing.MongoGrantId ??= dto.MongoGrantId;

        existing.OpportunityNumber = dto.OpportunityNumber ?? existing.OpportunityNumber;
        existing.SourceUrl         = dto.SourceUrl ?? existing.SourceUrl;
        existing.Summary           = dto.Summary ?? existing.Summary;
        existing.AgencyName        = dto.AgencyName ?? existing.AgencyName;
        existing.AgencyCode        = dto.AgencyCode ?? existing.AgencyCode;
        existing.Category          = dto.Category ?? existing.Category;
        existing.Currency          = string.IsNullOrWhiteSpace(dto.Currency) ? existing.Currency : dto.Currency;

        existing.AwardCeiling                 = dto.AwardCeiling ?? existing.AwardCeiling;
        existing.AwardFloor                   = dto.AwardFloor ?? existing.AwardFloor;
        existing.EstimatedTotalProgramFunding = dto.EstimatedTotalProgramFunding ?? existing.EstimatedTotalProgramFunding;
        existing.ExpectedNumberOfAwards       = dto.ExpectedNumberOfAwards ?? existing.ExpectedNumberOfAwards;
        existing.CostSharingRequired          = dto.CostSharingRequired ?? existing.CostSharingRequired;

        existing.PostedDate           = dto.PostedDate ?? existing.PostedDate;
        existing.CloseDate            = dto.CloseDate ?? existing.CloseDate;
        existing.LoiDueDate           = dto.LoiDueDate ?? existing.LoiDueDate;
        existing.ArchiveDate          = dto.ArchiveDate ?? existing.ArchiveDate;
        existing.CloseDateExplanation = dto.CloseDateExplanation ?? existing.CloseDateExplanation;
        existing.IsRolling            = dto.IsRolling || existing.IsRolling;

        existing.ApplicantTypesRaw  = dto.ApplicantTypesRaw is { Length: > 0 } ? dto.ApplicantTypesRaw : existing.ApplicantTypesRaw;
        existing.ApplicantTypeCodes = dto.ApplicantTypeCodes is { Length: > 0 } ? dto.ApplicantTypeCodes : existing.ApplicantTypeCodes;

        existing.TribalGovernmentsEligible   = dto.TribalGovernmentsEligible ?? existing.TribalGovernmentsEligible;
        existing.TribalOrganizationsEligible = dto.TribalOrganizationsEligible ?? existing.TribalOrganizationsEligible;
        existing.Nonprofit501C3Eligible      = dto.Nonprofit501C3Eligible ?? existing.Nonprofit501C3Eligible;
        existing.IsTribalSetAside            = dto.IsTribalSetAside ?? existing.IsTribalSetAside;
        existing.NativeLedPriority           = dto.NativeLedPriority ?? existing.NativeLedPriority;

        existing.AssistanceListingNumbers = dto.AssistanceListingNumbers is { Length: > 0 }
            ? dto.AssistanceListingNumbers : existing.AssistanceListingNumbers;
        existing.FundingInstruments = dto.FundingInstruments is { Length: > 0 }
            ? dto.FundingInstruments : existing.FundingInstruments;

        existing.IsForecast = dto.IsForecast;
        existing.Status     = NormalizeStatus(dto.Status);

        // Only overwrite AI fields when this run actually produced some. A seed-only re-scrape
        // sends AiScore 0 with no summary; taking that would erase a real score.
        if (dto.AiScore > 0) existing.AiScore = dto.AiScore;
        existing.AiSummary = dto.AiSummary ?? existing.AiSummary;
        existing.AiTags    = dto.AiTags is { Length: > 0 } ? dto.AiTags : existing.AiTags;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("[InternalGrant] Updated {Platform}/{GrantId} → {Id}",
            platform, dto.PlatformGrantId, existing.Id);

        return new UpsertGrantResponseDto(existing.Id, false);
    }

    /// <summary>
    /// A blank title renders as an empty row in every list. The tender line shipped 5,190 of them
    /// before a floor was added, so the floor is applied here at the boundary rather than trusted
    /// from upstream.
    /// </summary>
    private static string FallbackTitle(UpsertGrantDto dto) =>
        !string.IsNullOrWhiteSpace(dto.Title) ? dto.Title
        : !string.IsNullOrWhiteSpace(dto.OpportunityNumber) ? dto.OpportunityNumber
        : dto.PlatformGrantId;

    /// <summary>
    /// Maps to the <c>ck_grant_opportunities_status</c> vocabulary. An unknown value becomes
    /// <c>posted</c> rather than violating the check constraint and 500ing the ingest — the shadow
    /// index degrades, it does not block the pipeline.
    /// </summary>
    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "forecasted" => "forecasted",
        "closed"     => "closed",
        "archived"   => "archived",
        _            => "posted",
    };
}
