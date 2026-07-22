namespace BiddingBuddy.Bff.Core.DTOs.Grants;

/// <summary>
/// Body of <c>POST /internal/grants</c>, sent by BidProcessor's <c>EnrichGrantWorker</c>.
///
/// <para>Positional record with trailing optional parameters, matching <c>UpsertTenderDto</c>.
/// <b>New fields are appended with defaults, never inserted</b> — a positional record reorders
/// silently and the pipeline deploys independently of the BFF, so an older processor must keep
/// working against a newer BFF.</para>
/// </summary>
public record UpsertGrantDto(
    string Platform,
    string PlatformGrantId,
    string Title,
    string? MongoGrantId = null,
    string? OpportunityNumber = null,
    string? SourceUrl = null,
    string? Summary = null,
    string? AgencyName = null,
    string? AgencyCode = null,
    string? Category = null,
    string? Currency = null,
    decimal? AwardCeiling = null,
    decimal? AwardFloor = null,
    decimal? EstimatedTotalProgramFunding = null,
    int? ExpectedNumberOfAwards = null,
    bool? CostSharingRequired = null,
    DateOnly? PostedDate = null,
    DateOnly? CloseDate = null,
    DateOnly? LoiDueDate = null,
    DateOnly? ArchiveDate = null,
    string? CloseDateExplanation = null,
    bool IsRolling = false,
    string[]? ApplicantTypesRaw = null,
    string[]? ApplicantTypeCodes = null,
    bool? TribalGovernmentsEligible = null,
    bool? TribalOrganizationsEligible = null,
    bool? Nonprofit501C3Eligible = null,
    bool? IsTribalSetAside = null,
    bool? NativeLedPriority = null,
    string[]? AssistanceListingNumbers = null,
    string[]? FundingInstruments = null,
    bool IsForecast = false,
    string? Status = null,
    int AiScore = 0,
    string? AiSummary = null,
    string[]? AiTags = null);

public record UpsertGrantResponseDto(Guid GrantId, bool Created);

/// <summary>A grant as the SPA lists it.</summary>
public record GrantListItemDto(
    Guid Id,
    string? MongoGrantId,
    string PlatformGrantId,
    string Platform,
    string? OpportunityNumber,
    string Title,
    string? AgencyName,
    string? Category,
    string Currency,
    decimal? AwardCeiling,
    // Carried on the LIST, not just the detail: without a floor every row can only say
    // "Up to $X", which reads as a cap on an unknown range rather than the range itself.
    // Both stay nullable — an absent figure means the agency published none, and rendering
    // either as 0 would claim the grant awards nothing.
    decimal? AwardFloor,
    DateOnly? CloseDate,
    string? CloseDateExplanation,
    bool IsRolling,
    bool IsForecast,
    string Status,
    bool? TribalGovernmentsEligible,
    bool? IsTribalSetAside,
    int AiScore,
    int? DaysLeft);

/// <summary>A grant's full detail view.</summary>
public record GrantDetailDto(
    Guid Id,
    string? MongoGrantId,
    string PlatformGrantId,
    string Platform,
    string? OpportunityNumber,
    string? SourceUrl,
    string Title,
    string? Summary,
    string? Description,
    string? AgencyName,
    string? AgencyCode,
    string? Category,
    string Currency,
    decimal? AwardCeiling,
    decimal? AwardFloor,
    decimal? EstimatedTotalProgramFunding,
    int? ExpectedNumberOfAwards,
    bool? CostSharingRequired,
    decimal? CostSharePercentage,
    DateOnly? PostedDate,
    DateOnly? CloseDate,
    DateOnly? LoiDueDate,
    DateOnly? ArchiveDate,
    string? CloseDateExplanation,
    bool IsRolling,
    bool IsForecast,
    string Status,
    IReadOnlyList<string> ApplicantTypesRaw,
    IReadOnlyList<string> ApplicantTypeCodes,
    bool? TribalGovernmentsEligible,
    bool? TribalOrganizationsEligible,
    bool? Nonprofit501C3Eligible,
    bool? IsTribalSetAside,
    bool? NativeLedPriority,
    string? TribalRationale,
    string? EligibilityNarrative,
    IReadOnlyList<string> AssistanceListingNumbers,
    IReadOnlyList<string> FundingInstruments,
    int AiScore,
    string? AiSummary,
    IReadOnlyList<string> AiTags,
    IReadOnlyList<GrantDocumentDto> Documents,
    int? DaysLeft);

public record GrantDocumentDto(
    string DocumentId,
    string? Type,
    string? FileName,
    string? Url,
    bool HasStoredKey);

/// <summary>
/// Filter options that exist in the corpus, with the number of grants behind each.
///
/// <para>This endpoint exists because the alternative is a hardcoded copy of the Grants.gov
/// vocabulary in the client — 27 categories and 17 applicant types, most of which match nothing.
/// Serving only populated values, with counts, is the difference between a filter a user can
/// trust and one that teaches them their searches are broken.</para>
/// </summary>
public record GrantFacetsDto(
    IReadOnlyList<GrantFacetValueDto> Categories,
    IReadOnlyList<GrantFacetValueDto> ApplicantTypes,
    IReadOnlyList<GrantFacetValueDto> Agencies);

/// <param name="Value">Send this back as the filter value — a category label, applicant-type code, or agency name.</param>
/// <param name="Count">Grants this option returns under the same scope the facets were requested with.</param>
public record GrantFacetValueDto(string Value, long Count);

/// <summary>Scope for a facet request — mirrors the status view the client is showing.</summary>
public record GrantFacetRequestDto
{
    public string? Status { get; init; }
    public bool? IsForecast { get; init; }
    public string? Platform { get; init; }
    public int Limit { get; init; } = 50;
}

public record PagedGrantListDto(
    IReadOnlyList<GrantListItemDto> Items,
    long TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>Query parameters for the grant list endpoint.</summary>
public record GrantSearchRequestDto
{
    public string? Keyword { get; init; }
    public string? Category { get; init; }
    public string? Agency { get; init; }
    public string? Platform { get; init; }

    /// <summary>Grants.gov applicant-type CODE, e.g. <c>07</c> for federally recognized tribes.</summary>
    public string? ApplicantTypeCode { get; init; }

    public bool? TribalSetAsideOnly { get; init; }
    public bool? IsForecast { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ClosingAfter { get; init; }
    public DateTimeOffset? ClosingBefore { get; init; }
    public decimal? MinAwardCeiling { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
