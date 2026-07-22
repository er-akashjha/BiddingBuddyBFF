using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Infrastructure.Extensions;

/// <summary>
/// Maps the raw Mongo grant shape to the client DTOs — the grant-line sibling of
/// <c>TenderDetailsTranslator</c>.
///
/// <para><b>Uses <c>Guid.TryParse</c>, never <c>Guid.Parse</c>.</b> The tender translator does an
/// unguarded <c>Guid.Parse(tender.Id)</c> at two sites, so a single non-GUID row 500s an entire
/// list page — only one caller catches the <c>FormatException</c>. Grant ids are GUID-shaped by
/// construction in BiddingBuddyServices, but "by construction" is an invariant of another service
/// and a list endpoint must not be one bad row away from a 500.</para>
/// </summary>
public static class GrantDetailsTranslator
{
    /// <summary>
    /// Rows whose id will not parse are DROPPED from a list rather than throwing. Losing one row
    /// is recoverable and visible; losing the page is neither.
    /// </summary>
    public static List<GrantListItemDto> ToListDto(this IReadOnlyList<GrantSearchItemDto> items)
    {
        var result = new List<GrantListItemDto>(items.Count);

        foreach (var item in items)
        {
            if (!Guid.TryParse(item.Id, out var id)) continue;

            var closeDate = ToDateOnly(item.Timeline?.CloseAt);

            result.Add(new GrantListItemDto(
                Id:                id,
                MongoGrantId:      item.Id,
                PlatformGrantId:   item.Source?.PlatformGrantId ?? string.Empty,
                Platform:          item.Source?.Platform ?? "grants-gov",
                OpportunityNumber: item.Source?.OpportunityNumber,
                // Title floor, again at the read boundary: a blank renders as an empty row.
                Title:             FirstNonEmpty(item.Title, item.Source?.OpportunityNumber,
                                                 item.Source?.PlatformGrantId) ?? "Untitled opportunity",
                AgencyName:        item.Agency?.Name,
                Category:          item.Category?.Primary,
                Currency:          item.Funding?.Currency ?? "USD",
                AwardCeiling:      item.Funding?.AwardCeiling,
                AwardFloor:        item.Funding?.AwardFloor,
                CloseDate:         closeDate,
                CloseDateExplanation: item.Timeline?.CloseDateExplanation,
                IsRolling:         item.Timeline?.IsRolling ?? false,
                IsForecast:        item.IsForecast,
                Status:            item.Status?.State ?? "unknown",
                TribalGovernmentsEligible: item.Eligibility?.TribalGovernmentsEligible,
                IsTribalSetAside:  item.TribalIntelligence?.IsTribalSetAside,
                AiScore:           item.Ai?.OpportunityScore ?? 0,
                DaysLeft:          DaysLeft(closeDate)));
        }

        return result;
    }

    /// <summary>Null when the id is not GUID-shaped, so the caller can 404 rather than 500.</summary>
    public static GrantDetailDto? ToDetailDto(this GrantSearchItemDto g)
    {
        if (!Guid.TryParse(g.Id, out var id)) return null;

        var closeDate = ToDateOnly(g.Timeline?.CloseAt);

        return new GrantDetailDto(
            Id:                id,
            MongoGrantId:      g.Id,
            PlatformGrantId:   g.Source?.PlatformGrantId ?? string.Empty,
            Platform:          g.Source?.Platform ?? "grants-gov",
            OpportunityNumber: g.Source?.OpportunityNumber,
            SourceUrl:         g.Source?.SourceUrl,
            Title:             FirstNonEmpty(g.Title, g.Source?.OpportunityNumber,
                                             g.Source?.PlatformGrantId) ?? "Untitled opportunity",
            Summary:           g.Summary,
            Description:       g.Description,
            AgencyName:        g.Agency?.Name,
            AgencyCode:        g.Agency?.Code,
            Category:          g.Category?.Primary,
            Currency:          g.Funding?.Currency ?? "USD",
            // Money stays nullable all the way to the client. A 0 here would render as
            // "$0 award ceiling", asserting the grant funds nothing.
            AwardCeiling:      g.Funding?.AwardCeiling,
            AwardFloor:        g.Funding?.AwardFloor,
            EstimatedTotalProgramFunding: g.Funding?.EstimatedTotalProgramFunding,
            ExpectedNumberOfAwards:       g.Funding?.ExpectedNumberOfAwards,
            CostSharingRequired: g.Eligibility?.CostSharingRequired,
            CostSharePercentage: g.Eligibility?.CostSharePercentage,
            PostedDate:        ToDateOnly(g.Timeline?.PostedAt),
            CloseDate:         closeDate,
            LoiDueDate:        ToDateOnly(g.Timeline?.LoiDueAt),
            ArchiveDate:       ToDateOnly(g.Timeline?.ArchiveAt),
            // Carried verbatim: CloseDate alone cannot express rolling, "see NOFO", or a two-stage
            // LOI → full deadline, and the source publishes no cutoff time.
            CloseDateExplanation: g.Timeline?.CloseDateExplanation,
            IsRolling:         g.Timeline?.IsRolling ?? false,
            IsForecast:        g.IsForecast,
            Status:            g.Status?.State ?? "unknown",
            // Verbatim labels reach the client unchanged — the UI shows the user exactly what the
            // government wrote about who may apply.
            ApplicantTypesRaw:  g.Eligibility?.ApplicantTypesRaw ?? [],
            ApplicantTypeCodes: g.Eligibility?.ApplicantTypeCodes ?? [],
            TribalGovernmentsEligible:   g.Eligibility?.TribalGovernmentsEligible,
            TribalOrganizationsEligible: g.Eligibility?.TribalOrganizationsEligible,
            Nonprofit501C3Eligible:      g.Eligibility?.Nonprofit501C3Eligible,
            IsTribalSetAside:  g.TribalIntelligence?.IsTribalSetAside,
            NativeLedPriority: g.TribalIntelligence?.NativeLedPriority,
            TribalRationale:   g.TribalIntelligence?.Rationale,
            EligibilityNarrative: g.Eligibility?.Narrative,
            AssistanceListingNumbers: g.AssistanceListingNumbers ?? [],
            FundingInstruments:       g.Funding?.FundingInstruments ?? [],
            AiScore:           g.Ai?.OpportunityScore ?? 0,
            AiSummary:         g.Ai?.Summary,
            AiTags:            g.Ai?.Keywords ?? [],
            Documents:         (g.Documents ?? [])
                                   .Where(d => !string.IsNullOrWhiteSpace(d.DocumentId))
                                   .Select(d => new GrantDocumentDto(
                                       DocumentId:   d.DocumentId!,
                                       Type:         d.Type,
                                       FileName:     d.FileName,
                                       Url:          string.IsNullOrWhiteSpace(d.Url) ? null : d.Url,
                                       // S3 coords are never exposed; the client asks for a presign.
                                       HasStoredKey: !string.IsNullOrWhiteSpace(d.S3Key)))
                                   .ToList(),
            DaysLeft:          DaysLeft(closeDate));
    }

    private static DateOnly? ToDateOnly(DateTime? dt) =>
        dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>
    /// Whole days until the close DATE. Null for rolling/undated opportunities — which is why the
    /// client must render <c>CloseDateExplanation</c> rather than treating a null as "no deadline".
    /// </summary>
    private static int? DaysLeft(DateOnly? closeDate) =>
        closeDate is { } d ? d.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber : null;
}
