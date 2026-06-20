namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A client's saved "interest" — the criteria a freshly-enriched tender is tested
/// against. Org-scoped. A match buffers a <see cref="TenderMatch"/> row which is
/// later delivered as part of a digest email (see <see cref="OrgAlertSettings"/>).
/// All array/range fields are optional; an empty constraint is ignored (AND of the
/// constraints that are set).
/// </summary>
public class TenderAlertRule
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Name { get; set; } = default!;

    public string[]? Categories { get; set; }   // matches Tender.Category / SubCategory
    public string[]? States { get; set; }        // matches Tender.State
    public string[]? Keywords { get; set; }      // substring over Title / Description / AiTags
    public decimal? MinValue { get; set; }       // Tender.TenderValue >= MinValue
    public decimal? MaxValue { get; set; }       // Tender.TenderValue <= MaxValue
    public int? MinAiScore { get; set; }         // Tender.AiScore >= MinAiScore

    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
