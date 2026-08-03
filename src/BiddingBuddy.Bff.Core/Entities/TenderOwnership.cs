namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// "This organization owns this tender draft." Net-new state: tenders in this system are global and
/// carry no org id anywhere, because every one of them until now was scraped from a portal and
/// belonged to nobody.
///
/// <para>Kept as its own table rather than an <c>org_id</c> column on <see cref="TenderDraft"/>
/// — which the draft also has, as the fast path — because <c>delegate</c> is a real relationship:
/// a parent department authors a notice and hands day-to-day management to a subordinate office
/// without transferring ownership. The draft's own <c>org_id</c> answers "whose is this"; this
/// table answers "who may work on it".</para>
///
/// Maps to <c>tender_ownership</c> (migration 0031).
/// </summary>
public class TenderOwnership
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid DraftId { get; set; }

    /// <summary>owner | delegate.</summary>
    public string Relationship { get; set; } = "owner";

    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public TenderDraft Draft { get; set; } = default!;
}
