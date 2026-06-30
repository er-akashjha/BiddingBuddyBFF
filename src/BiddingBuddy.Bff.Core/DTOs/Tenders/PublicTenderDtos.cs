namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

// ── Public (anonymous / guest) tender DTOs ────────────────────────────────────
//
// These are the *only* shapes served to logged-out visitors via
// PublicTendersController (/api/public/tenders). They deliberately OMIT every
// org-specific and AI-scoring field — AiScore, EligibilityScore, WinProbability,
// RiskScore, AiSummary, AiAnalysis, OrgSettings (IsSaved/IsTracked) and
// CorrigendumCount — so the anonymous endpoint never leaks the personalized
// intelligence that is the whole reason to sign up. Only intrinsic, org-agnostic
// tender facts (scraped from the GeM corpus) cross the wire.
//
// Kept intentionally separate from TenderListItemDto / TenderDetailDto so future
// UI changes never widen the public contract by accident.
//
// NOTE: GemTenderId is deliberately ABSENT. The real GeM tender reference is never
// exposed to anonymous guests — otherwise they could take it straight to gem.gov.in
// and bypass the product. Authenticated endpoints (TenderListItemDto/TenderDetailDto)
// still carry it for paying customers.

public record PublicTenderListItemDto(
    Guid Id,
    string Title,
    string? BuyerOrgName,
    string? State,
    string? Category,
    decimal? TenderValue,
    decimal? EmdAmount,
    DateOnly? PublishedDate,
    DateOnly? ClosingDate,
    string Status
);

/// <summary>
/// Tender count for one state — for the public landing-page coverage map.
/// Mirrors BiddingBuddyServices' StateTenderCount.
/// </summary>
public record StateTenderCountDto(
    string State,
    long Count
);

public record PublicPagedTenderListDto(
    IReadOnlyList<PublicTenderListItemDto> Items,
    int  TotalCount,
    int  Page,
    int  PageSize,
    int  TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
);

public record PublicTenderDetailDto(
    Guid Id,
    string Title,
    string? Description,
    string? BuyerOrgName,
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
    string[]? Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // ── Structured procurement detail, passed through from BiddingBuddyServices (Mongo) ──
    // Org-agnostic, scraped from the tender document. Safe for guests.
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
    // ── Scraped source documents (metadata only — never s3Bucket/s3Key) ──
    IReadOnlyList<TenderSourceDocumentDto> SourceDocuments,
    // ── Full timeline ──
    TenderTimelineDto? Timeline
);
