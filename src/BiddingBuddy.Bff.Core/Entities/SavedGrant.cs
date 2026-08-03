namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// An org's saved / tracked grant opportunity (the star/plus on the discovery list). Org-scoped,
/// unlike the global <see cref="GrantOpportunity"/> corpus. Holds a client-supplied snapshot of the
/// grant's display fields so the saved list needs no Mongo round-trip; <see cref="MongoGrantId"/> is
/// the dedup key and the deep-link back to the opportunity.
/// </summary>
public class SavedGrant
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string MongoGrantId { get; set; } = default!;

    public string Title { get; set; } = default!;
    public string? AgencyName { get; set; }
    public string? OpportunityNumber { get; set; }
    public string? Category { get; set; }
    public DateOnly? CloseDate { get; set; }
    public decimal? AwardCeiling { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsForecast { get; set; }
    public string? SourceUrl { get; set; }

    public Guid? SavedBy { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
