namespace BiddingBuddy.Bff.Core.Entities;

public class Tender
{
    public Guid Id { get; set; }
    public string GemTenderId { get; set; } = default!;

    // The tender's MongoDB _id in BiddingBuddyServices — the portal-independent id the
    // SPA's /tenders/:id route resolves by. Stored so digest-email links point at the
    // right tender (the Postgres Id is a different id-space). NULL until the pipeline
    // re-enriches this tender after migration 0010.
    public string? MongoTenderId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string? BuyerOrgName { get; set; }
    public string? BuyerOrgIdGem { get; set; }
    public string? State { get; set; }
    public string? City { get; set; }
    public string? Category { get; set; }
    public string? SubCategory { get; set; }
    public decimal? TenderValue { get; set; }
    public decimal? EmdAmount { get; set; }
    public DateOnly? PublishedDate { get; set; }
    public DateOnly? ClosingDate { get; set; }
    public int? DeliveryDays { get; set; }
    public string Status { get; set; } = "active";
    public int CorrigendumCount { get; set; }
    public int? AiScore { get; set; }
    public int? EligibilityScore { get; set; }
    public decimal? WinProbability { get; set; }
    public int? RiskScore { get; set; }
    public string? AiSummary { get; set; }
    public string[]? AiTags { get; set; }
    public string? RawData { get; set; }             // JSON
    public string Source { get; set; } = "gem_pipeline";

    // Source portal this tender was scraped from ("gem" | "eprocure" | "ireps" | …). Distinct
    // from Source (a coarse pipeline tag) — this is per-tender provenance sent by the downloader
    // via the enrichment envelope, so the UI can badge/filter by portal. Defaults to "gem".
    public string? Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Set once the scheduled tender-alert scan has evaluated this tender against
    // interest rules. NULL = not yet evaluated (the scan's work-list).
    public DateTime? AlertsScannedAt { get; set; }

    public ICollection<TenderDocument> Documents { get; set; } = [];
    public ICollection<OrgTenderSettings> OrgSettings { get; set; } = [];
    public AiAnalysisResult? AiAnalysis { get; set; }
}
