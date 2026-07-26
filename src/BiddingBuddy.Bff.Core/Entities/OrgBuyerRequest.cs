namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// An organization asking the platform to become a buyer — able to publish tender notices.
///
/// <para>The mirror of <see cref="OrgJoinRequest"/> one level up: there a person asks an org to let
/// them in, here an org asks the platform to let it publish. Both share the rule that matters —
/// the request grants nothing. An <b>operator</b> decides, because a buyer publishes notices on the
/// public portal under a department's name and that trust cannot be self-asserted.</para>
///
/// <para>The claimed procuring-entity identity is carried on the request for two reasons: it is what
/// the operator verifies against, and on approval it is written onto the organization verbatim
/// rather than retyped.</para>
///
/// Maps to <c>org_buyer_requests</c> (migration 0033).
/// </summary>
public class OrgBuyerRequest
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>The owner/admin who raised it.</summary>
    public Guid RequestedBy { get; set; }

    /// <summary>pending | approved | rejected | cancelled. A partial unique index allows exactly one
    /// <c>pending</c> row per org; decided rows accumulate as history so a rejected org can reapply.</summary>
    public string Status { get; set; } = "pending";

    // ── Claimed procuring-entity identity (verification target; written to the org on approval) ──
    public string? EntityType { get; set; }
    public string? Ministry { get; set; }
    public string? Department { get; set; }
    public string? Office { get; set; }
    public string? ProcuringEntityCode { get; set; }

    /// <summary>Why this org should be trusted to publish under a government name. Mandatory — an
    /// approval is a judgement, and a judgement needs something to judge.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>The operator's note: the evidence checked, or the reason for refusal. Shown to the
    /// org on rejection.</summary>
    public string? DecisionNote { get; set; }

    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User Requester { get; set; } = default!;
}
