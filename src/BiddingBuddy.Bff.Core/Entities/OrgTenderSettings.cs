namespace BiddingBuddy.Bff.Core.Entities;

public class OrgTenderSettings
{
    public Guid OrgId { get; set; }
    public Guid TenderId { get; set; }
    public bool IsTracked { get; set; }
    public bool IsSaved { get; set; }
    public int? CustomScore { get; set; }
    public string? Notes { get; set; }
    public string[]? Tags { get; set; }
    public Guid? AddedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Tender Tender { get; set; } = default!;
}
