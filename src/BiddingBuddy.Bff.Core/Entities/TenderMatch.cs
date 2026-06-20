namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Buffer + dedup row: one tender matched one org's interest. Sits in
/// <c>pending</c> until a digest flush groups it with others into a single
/// notification (then <c>sent</c>), or its tender's deadline passes first
/// (<c>expired</c>). The UNIQUE (org_id, tender_id) constraint guarantees a
/// tender is only ever queued once per org, even if it matches several rules
/// or is re-enriched.
/// </summary>
public class TenderMatch
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid TenderId { get; set; }
    public Guid? RuleId { get; set; }            // the rule that first matched (null if rule later deleted)
    public string Status { get; set; } = "pending";  // pending | sent | expired
    public DateTime MatchedAt { get; set; }
    public Guid? BatchId { get; set; }           // groups the matches delivered together
    public DateTime? SentAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Tender Tender { get; set; } = default!;
}
