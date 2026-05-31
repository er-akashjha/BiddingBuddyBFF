namespace BiddingBuddy.Bff.Core.Entities;

public class CompetitorBidObservation
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid CompetitorId { get; set; }
    public Guid? TenderId { get; set; }
    public string GemTenderId { get; set; } = default!;
    public decimal? ObservedBidValue { get; set; }
    public bool WasWinner { get; set; }
    public decimal? AwardedValue { get; set; }
    public DateOnly? ObservedDate { get; set; }
    public string? RawData { get; set; }
    public DateTime CreatedAt { get; set; }

    public Competitor Competitor { get; set; } = default!;
    public Tender? Tender { get; set; }
}
