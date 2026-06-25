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
    AiAnalysisResultDto? AiAnalysis,
    // ── Structured procurement detail, passed through from BiddingBuddyServices (Mongo) ──
    TenderFinancialDto? Financial,
    TenderQualificationDto? Qualification,
    TenderCommercialDto? Commercial,
    TenderComplianceDto? Compliance,
    IReadOnlyList<TenderItemDto> Items,
    // ── Buyer / organization detail ──
    string? Ministry,
    string? Department,
    string? Office,
    string? BuyerName,
    string? BuyerDesignation,
    // ── Scraped source documents (the actual GeM PDFs in S3) ──
    IReadOnlyList<TenderSourceDocumentDto> SourceDocuments,
    // ── Full timeline (publishedAt / bidStartAt / bidEndAt / bidOpeningAt / validityDays / contractDuration) ──
    TenderTimelineDto? Timeline
);

/// <summary>
/// A document scraped from the source platform and stored in S3. The file is
/// served to the client via a short-lived presigned URL — the BFF never streams
/// the bytes itself. <see cref="HasStoredKey"/> is false for older tenders that
/// were enriched before s3Key was persisted; the download endpoint falls back to
/// reconstructing the key from the platform tender id + document id.
/// </summary>
public record TenderSourceDocumentDto(
    string DocumentId,
    string? Type,
    string? FileName,
    bool HasStoredKey,
    // Direct external URL on the source platform (GeM). Set for display-only buyer
    // attachments; null for files stored in our S3 (downloaded via the presign endpoint).
    string? Url = null
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
