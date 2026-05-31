namespace BiddingBuddy.Bff.Core.Entities;

public class OrgPerformanceSnapshot
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int? TotalBids { get; set; }
    public int? BidsWon { get; set; }
    public int? BidsLost { get; set; }
    public decimal? WinRate { get; set; }
    public decimal? TotalBidValue { get; set; }
    public decimal? WonValue { get; set; }
    public decimal? AvgBidValue { get; set; }
    public string? TopCategories { get; set; }  // JSON
    public string? TopStates { get; set; }      // JSON
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
