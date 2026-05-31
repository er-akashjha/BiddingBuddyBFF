namespace BiddingBuddy.Bff.Core.Entities;

public class Competitor
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string CompanyName { get; set; } = default!;
    public string? GemSellerId { get; set; }
    public string? Tier { get; set; }          // tier1|tier2|tier3
    public string? ThreatLevel { get; set; }   // high|medium|low
    public decimal? WinRate { get; set; }
    public int TotalContracts { get; set; }
    public decimal? TotalWinValue { get; set; }
    public decimal? AvgBidValue { get; set; }
    public string[]? ActiveStates { get; set; }
    public string[]? ActiveCategories { get; set; }
    public DateOnly? FirstSeenAt { get; set; }
    public DateOnly? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public ICollection<CompetitorBidObservation> BidObservations { get; set; } = [];
}
