namespace BiddingBuddy.Bff.Core.DTOs.Internal;

public record UpsertTenderDto(
    string GemTenderId,
    string? MongoTenderId,
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
    string? RawData,
    // Source portal ("gem" | "eprocure" | "ireps" | …). Sent by BidProcessor from the
    // downloader envelope's Platform; optional so pre-existing callers stay valid (defaults null → "gem").
    string? Platform = null
);

public record UpsertTenderResponseDto(Guid TenderId, bool Created);

/// <summary>
/// Outcome of one backfill batch (<c>POST /internal/tenders/backfill-mongo-id</c>).
/// <para><c>Scanned</c> = rows examined this batch · <c>Updated</c> = mongo_tender_id filled ·
/// <c>NotFound</c> = no matching Mongo tender for the gem id · <c>Failed</c> = lookup/persist error ·
/// <c>Remaining</c> = rows still NULL after this batch (call again until 0).</para>
/// </summary>
public record BackfillTenderMongoIdResultDto(
    int Scanned,
    int Updated,
    int NotFound,
    int Failed,
    int Remaining
);

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
