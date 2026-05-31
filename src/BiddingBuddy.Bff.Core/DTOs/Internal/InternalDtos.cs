namespace BiddingBuddy.Bff.Core.DTOs.Internal;

public record UpsertTenderDto(
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
    string? Status,
    int? CorrigendumCount,
    int? AiScore,
    int? EligibilityScore,
    decimal? WinProbability,
    int? RiskScore,
    string? AiSummary,
    string[]? AiTags,
    string? RawData
);

public record UpsertTenderResponseDto(Guid TenderId, bool Created);

public record UpsertDocumentContentDto(
    string FileName,
    string S3Key,
    string? DocumentType,
    int? FileSizeKb,
    string? ExtractedText
);

public record UpsertCompetitorDto(
    Guid OrgId,
    string CompanyName,
    string? GemSellerId,
    string? Tier,
    string? ThreatLevel,
    decimal? WinRate,
    int? TotalContracts,
    decimal? TotalWinValue,
    decimal? AvgBidValue,
    string[]? ActiveStates,
    string[]? ActiveCategories,
    DateOnly? FirstSeenAt,
    DateOnly? LastSeenAt
);

public record UpsertAiAnalysisDto(
    string GemTenderId,
    string? ModelUsed,
    string? EligibilityBreakdown,
    string? RiskFactors,
    string? WinStrategy,
    string? SuggestedBidRange,
    string[]? RequiredDocuments,
    string[]? KeyClauses,
    string? RawResponse
);
