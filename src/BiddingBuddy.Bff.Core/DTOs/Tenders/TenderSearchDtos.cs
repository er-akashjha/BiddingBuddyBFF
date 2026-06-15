namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>Query parameters forwarded to BiddingBuddyServices /api/tenders/search</summary>
public record TenderSearchQueryDto
{
    public string?   NameContains      { get; init; }
    public string?   SourceTenderID    { get; init; }
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
    string? BuyerName,
    string? BuyerDesignation
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

public record TenderEpbgDto(bool Required, double? Percentage, int? DurationMonths, string? Bank);

public record TenderTurnoverDto(decimal? Bidder, decimal? Oem);

public record TenderFinancialDto(
    decimal?            EstimatedBidValue,
    TenderEmdDto?       Emd,
    TenderEpbgDto?      Epbg,
    TenderTurnoverDto?  TurnoverRequirement
);

public record TenderQualificationDto(
    int?      ExperienceYears,
    double?   PastPerformancePercentage,
    bool      StartupRelaxation,
    bool      MseRelaxation,
    string[]? RequiredDocuments,
    string[]? Certifications
);

public record TenderReverseAuctionDto(bool Enabled, string? Rule);

public record TenderCommercialDto(
    string?                 EvaluationMethod,
    string?                 BidType,
    TenderReverseAuctionDto? ReverseAuction,
    bool                    BidSplitting
);

public record TenderComplianceDto(
    bool    MiiPreference,
    bool    MsePreference,
    double? PurchasePreferencePercent
);

public record TenderItemDto(
    string?  Name,
    string?  Category,
    string?  Unit,
    double?  Quantity,
    decimal? UnitPrice,
    decimal? TotalAmount,
    string?  Specifications
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

public record TenderDocumentRefDto(string? Type, string? FileName, string? DocumentId, string? S3Bucket, string? S3Key);

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
    TenderQualificationDto? Qualification,
    TenderCommercialDto? Commercial,
    TenderComplianceDto? Compliance,
    IReadOnlyList<TenderItemDto>? Items,
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

/// <summary>Paged tender list returned to the BFF client (items translated to TenderListItemDto)</summary>
public record PagedTenderListDto(
    IReadOnlyList<TenderListItemDto> Items,
    int  TotalCount,
    int  Page,
    int  PageSize,
    int  TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
);
