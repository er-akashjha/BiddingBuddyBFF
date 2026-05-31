namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

public record TenderListItemDto(
    Guid Id,
    string GemTenderId,
    string Title,
    string? BuyerOrgName,
    string? State,
    string? Category,
    decimal? TenderValue,
    decimal? EmdAmount,
    DateOnly? PublishedDate,
    DateOnly? ClosingDate,
    string Status,
    int? AiScore,
    decimal? WinProbability,
    bool IsTracked,
    bool IsSaved
);

public record TenderDetailDto(
    Guid Id,
    string GemTenderId,
    string Title,
    string? Description,
    string? BuyerOrgName,
    string? BuyerOrgIdGem,
    string? State,
    string? City,
    string? Category,
    string? SubCategory,
    decimal? TenderValue,
    decimal? EmdAmount,
    DateOnly? PublishedDate,
    DateOnly? ClosingDate,
    int? DeliveryDays,
    string Status,
    int CorrigendumCount,
    int? AiScore,
    int? EligibilityScore,
    decimal? WinProbability,
    int? RiskScore,
    string? AiSummary,
    string[]? AiTags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<TenderDocumentDto> Documents,
    OrgTenderSettingsDto? OrgSettings,
    AiAnalysisResultDto? AiAnalysis
);

public record TenderDocumentDto(
    Guid Id,
    string FileName,
    string? DocumentType,
    int? FileSizeKb,
    DateTime CreatedAt
);

public record OrgTenderSettingsDto(
    bool IsTracked,
    bool IsSaved,
    int? CustomScore,
    string? Notes,
    string[]? Tags
);

public record AiAnalysisResultDto(
    Guid Id,
    string? ModelUsed,
    string? EligibilityBreakdown,
    string? RiskFactors,
    string? WinStrategy,
    string? SuggestedBidRange,
    string[]? RequiredDocuments,
    string[]? KeyClauses,
    DateTime GeneratedAt
);

public record SaveTenderDto(string? Notes, string[]? Tags, int? CustomScore);

public record TenderFiltersDto(
    string? Search,
    string? Category,
    string? State,
    string? Status,
    decimal? MinValue,
    decimal? MaxValue,
    DateOnly? ClosingAfter,
    DateOnly? ClosingBefore,
    bool? TrackedOnly,
    bool? SavedOnly,
    int Page = 1,
    int PageSize = 20
);
