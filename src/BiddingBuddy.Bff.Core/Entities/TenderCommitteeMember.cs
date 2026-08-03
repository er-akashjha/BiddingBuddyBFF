namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A named member of a tender's opening, technical-evaluation or financial-evaluation committee,
/// or the Independent External Monitor where an Integrity Pact applies.
///
/// <para>In Phase 1 this record is documentary: it is what the published notice names and what the
/// audit file reports. In Phase 3 the opening committee becomes the M-of-N key holder set, which is
/// why the shape is fixed now — committee membership recorded on a Phase-1 tender should still mean
/// something when sealed bids land.</para>
///
/// <para><see cref="UserId"/> is nullable and the name is stored alongside it, because a committee
/// routinely includes someone from another department who has no account here, and because the
/// audit file must still name whoever sat on the committee after their membership is suspended.</para>
///
/// Maps to <c>tender_committee_members</c> (migration 0031).
/// </summary>
public class TenderCommitteeMember
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }

    /// <summary>NULL for an external member with no account in this system.</summary>
    public Guid? UserId { get; set; }

    /// <summary>opening | technical | financial | monitor.</summary>
    public string Committee { get; set; } = string.Empty;

    public string MemberName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsChair { get; set; }
    public DateTime CreatedAt { get; set; }

    public TenderDraft Draft { get; set; } = default!;
}
