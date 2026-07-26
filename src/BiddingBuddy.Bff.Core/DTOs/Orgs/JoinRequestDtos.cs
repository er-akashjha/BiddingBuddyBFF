namespace BiddingBuddy.Bff.Core.DTOs.Orgs;

// ── Duplicate detection ──────────────────────────────────────────────────────

/// <summary>
/// What the client sees when signup is refused as a duplicate — the body of the 409.
/// </summary>
/// <param name="Error">Always <c>ORG_EXISTS</c>. Matches the <c>{ error }</c> shape the
/// SPA already branches on for <c>ALREADY_MEMBER</c> and <c>INVITE_INVALID</c>.</param>
/// <param name="Match"><c>gstin</c> or <c>name</c> — which signal fired. The client words
/// the two differently because only one of them is certain.</param>
/// <param name="CanOverride">True only for a name match. A GSTIN identifies one legal
/// entity, so there is no honest "create it anyway" for that case.</param>
/// <param name="ExistingRequest">The caller's own live request against this org, when they
/// already have one — so re-running onboarding shows the waiting state instead of
/// re-offering a button that would do nothing.</param>
public record OrgExistsDto(
    string Error,
    string Match,
    bool CanOverride,
    DuplicateOrgSummaryDto Org,
    MyJoinRequestDto? ExistingRequest
);

/// <summary>
/// The minimum a stranger needs to recognise their own employer, and nothing more.
///
/// <para>Deliberately carries no admin name or email, even masked: the join-request flow
/// routes to admins automatically, so the client never needs to answer "who do I ask",
/// and publishing it would hand whoever guessed a GSTIN a named target. GSTINs are public
/// information in India — an org name and city are not a meaningful additional disclosure;
/// a named administrator would be.</para>
/// </summary>
public record DuplicateOrgSummaryDto(
    Guid Id,
    string Name,
    string? City,
    int MemberCount
);

// ── Join requests ────────────────────────────────────────────────────────────

/// <param name="Message">Optional free-text note shown to the approver.</param>
public record CreateJoinRequestDto(Guid OrgId, string? Message);

/// <summary>One of the caller's own requests — powers the onboarding waiting state.</summary>
public record MyJoinRequestDto(
    Guid Id,
    Guid OrgId,
    string OrgName,
    string? OrgLogoUrl,
    string Status,
    string? Role,
    DateTime CreatedAt,
    DateTime? DecidedAt
);

/// <summary>One pending request in an org's approval queue (owner/admin view).</summary>
public record OrgJoinRequestDto(
    Guid Id,
    Guid UserId,
    string UserName,
    string UserEmail,
    string? UserAvatarUrl,
    string? Message,
    DateTime CreatedAt
);

/// <param name="Role">The role to grant. Chosen by the approver, never by the requester —
/// otherwise asking for <c>owner</c> and being handed it is a one-line privilege escalation.</param>
public record ApproveJoinRequestDto(string Role);

public record JoinRequestResultDto(
    Guid Id,
    Guid OrgId,
    string OrgName,
    string Status
);
