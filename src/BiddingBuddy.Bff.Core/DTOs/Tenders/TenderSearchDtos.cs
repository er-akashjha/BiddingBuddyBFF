namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>Query parameters forwarded to BiddingBuddyServices /api/tenders/search</summary>
public record TenderSearchQueryDto
{
    public string?   NameContains      { get; init; }
    public string?   Status            { get; init; }
    public string?   CategoryPrimary   { get; init; }
    public string?   CategorySecondary { get; init; }
    public string?   Tag               { get; init; }
    public string?   Organization      { get; init; }
    public string?   Ministry          { get; init; }
    public string?   State             { get; init; }
    public string?   City              { get; init; }
    public DateTime? BidEndFrom        { get; init; }
    public DateTime? BidEndTo          { get; init; }
    public decimal?  MinValue          { get; init; }
    public decimal?  MaxValue          { get; init; }
    public string?   SortBy            { get; init; }
    public string?   SortOrder         { get; init; }
    public int       Page              { get; init; } = 1;
    public int       PageSize          { get; init; } = 20;
}

// ── Nested sub-objects ────────────────────────────────────────────────────────

public record TenderSourceDto(
    string? Platform,
    string? PlatformTenderId,
    string? ExternalBidNumber,
    DateTime? ImportedAt
);

public record TenderCategoryDto(
    string?    Primary,
    string?    Secondary,
    string[]?  Tags
);

public record TenderOrganizationDto(
    string? Ministry,
    string? Department,
    string? Organization,
    string? Office,
    string? BuyerName
);

public record TenderLocationDto(
    string? Country,
    string? State,
    string? City
);

public record TenderTimelineDto(
    DateTime? PublishedAt,
    DateTime? BidStartAt,
    DateTime? BidEndAt,
    DateTime? BidOpeningAt,
    int?      ValidityDays,
    string?   ContractDuration    // stored as a free-text string, e.g. "6 months", "1 year"
);

public record TenderEmdDto(bool Required, decimal? Amount, string? Bank);

public record TenderFinancialDto(
    decimal?    EstimatedBidValue,
    TenderEmdDto? Emd
);

public record TenderAiDto(
    string?   NormalizedTitle,
    string[]? Keywords,
    string?   Industry,
    int       OpportunityScore,
    int       RiskScore,
    string[]? RecommendedFor,
    string?   Summary
    // embedding intentionally excluded — large float array, not needed by client
);

public record TenderStatusDto(
    string? State,
    bool    IsArchived,
    bool    IsCancelled
);

public record TenderDocumentRefDto(string? Type, string? FileName, string? DocumentId);

public record TenderTechSpecDto(string? Group, string? Name, string? Value);

/// <summary>Single tender item as returned by BiddingBuddyServices (embedding excluded)</summary>
public record TenderSearchItemDto(
    string               Id,
    TenderSourceDto?     Source,
    string?              Title,
    string?              Summary,
    TenderCategoryDto?   Category,
    TenderOrganizationDto? Organization,
    TenderLocationDto?   Location,
    TenderTimelineDto?   Timeline,
    TenderFinancialDto?  Financial,
    TenderAiDto?         Ai,
    TenderStatusDto?     Status,
    IReadOnlyList<TenderDocumentRefDto>? Documents,
    IReadOnlyList<TenderTechSpecDto>?    TechnicalSpecifications,
    DateTime             CreatedAt,
    DateTime             UpdatedAt
);

/// <summary>Paged result wrapper returned by BiddingBuddyServices and forwarded to the client</summary>
public record TenderSearchResultDto(
    IReadOnlyList<TenderSearchItemDto> Items,
    int  TotalCount,
    int  Page,
    int  PageSize,
    int  TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
);
