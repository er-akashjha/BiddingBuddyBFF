namespace BiddingBuddy.Bff.Core.Entities;

public class AiAnalysisResult
{
    public Guid Id { get; set; }
    public Guid TenderId { get; set; }
    public string? ModelUsed { get; set; }
    public string? EligibilityBreakdown { get; set; }  // JSON
    public string? RiskFactors { get; set; }            // JSON
    public string? WinStrategy { get; set; }
    public string? SuggestedBidRange { get; set; }      // JSON: {min, max, recommended}
    public string[]? RequiredDocuments { get; set; }
    public string[]? KeyClauses { get; set; }
    public string? RawResponse { get; set; }
    public DateTime GeneratedAt { get; set; }

    public Tender Tender { get; set; } = default!;
}
