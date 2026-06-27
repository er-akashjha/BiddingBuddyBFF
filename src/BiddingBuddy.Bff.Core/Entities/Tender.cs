namespace BiddingBuddy.Bff.Core.Entities;

public class Tender
{
    public Guid Id { get; set; }
    public string GemTenderId { get; set; } = default!;
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Set once the scheduled tender-alert scan has evaluated this tender against
    // interest rules. NULL = not yet evaluated (the scan's work-list).
    public DateTime? AlertsScannedAt { get; set; }

    // Global AI-enrichment lifecycle (mirrored from the pipeline):
    //   none | extracted | queued | processing | enriched | failed
    public string EnrichmentStatus { get; set; } = "none";

    // Set once the "AI analysis ready" notification has been sent for this tender,
    // so a re-enrich / repeated mirror doesn't re-email.
    public DateTime? EnrichmentNotifiedAt { get; set; }

    public ICollection<TenderDocument> Documents { get; set; } = [];
    public ICollection<OrgTenderSettings> OrgSettings { get; set; } = [];
    public AiAnalysisResult? AiAnalysis { get; set; }
}
