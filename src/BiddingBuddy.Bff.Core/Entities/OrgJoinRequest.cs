namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A person asking to be let into an organization that already exists — the mirror
/// image of <see cref="OrganizationInvite"/>, which is the organization reaching out
/// to a person. Raised when signup is blocked as a duplicate (same GSTIN, or the user
/// recognised their company by name).
///
/// <para>Both directions share the rule that matters: membership is never granted by
/// the request itself. An owner or admin decides, and picks the role while deciding.</para>
///
/// Maps to <c>org_join_requests</c>.
/// </summary>
public class OrgJoinRequest
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>pending | approved | rejected | cancelled. A partial unique index allows
    /// exactly one <c>pending</c> row per (org, user); decided rows accumulate as history.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Optional note from the requester — "I'm the new bid manager", etc.</summary>
    public string? Message { get; set; }

    /// <summary>The role granted on approval. NULL while pending: the approver chooses it
    /// at decision time, so a requester cannot ask for <c>owner</c> and be handed it.</summary>
    public string? Role { get; set; }

    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User User { get; set; } = default!;
}
