using BiddingBuddy.Bff.Core.DTOs.Grants;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Reads the authoritative grant record from BiddingBuddyServices (MongoDB-backed), exactly as
/// the tender read path proxies <c>tenders</c>.
///
/// <para>Implemented by <c>BiddingBuddyServicesClient</c> rather than a separate class, so grants
/// share its cached JWT. A second client would maintain a second token and double the
/// <c>POST /auth/token</c> traffic for no benefit.</para>
///
/// <para><b>Widening a grant DTO later means touching four places:</b> the raw search DTO here,
/// the DTO record in <c>GrantDtos.cs</c> (append trailing params — they are positional records),
/// <c>GrantDetailsTranslator</c>, and <c>BuildGrantSearchUrl</c> if it is also a filter.</para>
/// </summary>
public interface IGrantServicesClient
{
    /// <summary>Full grant record by its GUID-shaped Mongo <c>_id</c>. Null when not found.</summary>
    Task<GrantSearchItemDto?> GetRawGrantAsync(string grantId, CancellationToken ct = default);

    /// <summary>Paged grant search proxied to <c>/api/grants/search</c>.</summary>
    Task<RawGrantPageDto> SearchGrantsAsync(GrantSearchRequestDto query, CancellationToken ct = default);

    /// <summary>
    /// Populated filter options with counts, proxied to <c>/api/grants/facets</c>. Returns empty
    /// lists rather than null when upstream is unreachable — a filter with no options degrades to
    /// "no filtering available", which is recoverable; a null would take the whole page down.
    /// </summary>
    Task<RawGrantFacetsDto> GetGrantFacetsAsync(GrantFacetRequestDto query, CancellationToken ct = default);
}

// ── Raw wire shapes from BiddingBuddyServices ────────────────────────────────
// Nullable throughout: this is another service's schema, and a missing block must degrade the
// response rather than throw. Names match the camelCase Mongo document.

public record RawGrantPageDto(
    List<GrantSearchItemDto>? Items,
    long TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public record GrantSearchItemDto(
    string Id,
    GrantSourceItemDto? Source,
    string? Title,
    string? Summary,
    string? Description,
    GrantAgencyItemDto? Agency,
    GrantCategoryItemDto? Category,
    List<string>? AssistanceListingNumbers,
    GrantEligibilityItemDto? Eligibility,
    GrantFundingItemDto? Funding,
    GrantTimelineItemDto? Timeline,
    GrantTribalItemDto? TribalIntelligence,
    List<GrantDocumentItemDto>? Documents,
    GrantAiItemDto? Ai,
    GrantStatusItemDto? Status,
    bool IsForecast,
    DateTime? CreatedAt,
    DateTime? UpdatedAt);

public record GrantSourceItemDto(
    string? Platform,
    string? PlatformGrantId,
    string? OpportunityNumber,
    string? SourceUrl);

public record GrantAgencyItemDto(string? Name, string? Code);

public record GrantCategoryItemDto(string? Primary, List<string>? Secondary, List<string>? Tags);

public record GrantEligibilityItemDto(
    List<string>? ApplicantTypesRaw,
    List<string>? ApplicantTypeCodes,
    string? Narrative,
    bool? TribalGovernmentsEligible,
    bool? TribalOrganizationsEligible,
    bool? Nonprofit501C3Eligible,
    bool? CostSharingRequired,
    decimal? CostSharePercentage);

public record GrantFundingItemDto(
    string? Currency,
    decimal? AwardCeiling,
    decimal? AwardFloor,
    decimal? EstimatedTotalProgramFunding,
    int? ExpectedNumberOfAwards,
    List<string>? FundingInstruments,
    string? OpportunityCategory);

public record GrantTimelineItemDto(
    DateTime? PostedAt,
    DateTime? CloseAt,
    long? CloseAtEpochMs,
    DateTime? ArchiveAt,
    DateTime? LastUpdatedAt,
    DateTime? LoiDueAt,
    string? CloseDateExplanation,
    bool IsRolling);

public record GrantTribalItemDto(bool? IsTribalSetAside, bool? NativeLedPriority, string? Rationale);

public record GrantDocumentItemDto(
    string? DocumentId,
    string? Type,
    string? FileName,
    string? Url,
    string? S3Bucket,
    string? S3Key);

public record GrantAiItemDto(
    string? Summary,
    List<string>? Keywords,
    int OpportunityScore,
    string? Provider,
    DateTime? GeneratedAt);

public record GrantStatusItemDto(string? State);

public record RawGrantFacetsDto(
    List<RawGrantFacetValueDto>? Categories,
    List<RawGrantFacetValueDto>? ApplicantTypes,
    List<RawGrantFacetValueDto>? Agencies);

public record RawGrantFacetValueDto(string? Value, long Count);
