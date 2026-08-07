namespace BiddingBuddy.Bff.Core.DTOs.Orgs;

/// <param name="AllowDuplicateName">Set by the client only after the user has been shown an
/// existing organization with a matching name and chosen to create a separate workspace
/// anyway. Ignored for a GSTIN match, which has no override. Appended last and defaulted so
/// the existing positional constructions keep compiling.</param>
public record CreateOrgDto(
    string Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? GemSellerName,
    string? PrimaryCategory,
    bool AllowDuplicateName = false,

    /// <summary>
    /// Which plan this workspace starts on. Omitted (the default) means the 14-day Growth
    /// trial the marketing site promises. Pass <c>"free"</c> to decline it — someone who
    /// picked Free on the pricing page should land on Free, not on a trial clock they have
    /// to sit out before getting the plan they actually chose.
    ///
    /// <para>Only "free" is honored. Any other value falls back to the trial rather than
    /// erroring, so this can never become a way to self-assign a paid plan.</para>
    /// </summary>
    string? StartPlan = null
);

public record UpdateOrgDto(
    string? Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? GemSellerName,
    string? PrimaryCategory,
    string? LogoUrl
);

public record OrgDetailDto(
    Guid Id,
    string Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? GemSellerName,
    string? PrimaryCategory,
    string? LogoUrl,
    bool IsActive,
    string UserRole,
    DateTime CreatedAt,
    IReadOnlyList<OrgMemberDto> Members
);

public record OrgMemberDto(
    Guid Id,
    Guid UserId,
    string Name,
    string Email,
    string? AvatarUrl,
    string Role,
    string? Department,
    string Status,
    DateTime? JoinedAt,

    /// <summary>Bids currently assigned to this member's user id with <c>status_category = 'open'</c>.</summary>
    int ActiveBidsCount,

    /// <summary>won / (won + lost) as a 0–100 percentage. <c>null</c> when the member has no decided bids.</summary>
    decimal? WinRate,

    /// <summary>Checklist items this user completed (<c>done_by = user_id AND is_done</c>), org-wide.</summary>
    int TasksDoneCount
);

/// <summary>One org-wide activity feed entry — a <c>bid_activities</c> row joined to actor + bid.</summary>
public record OrgActivityDto(
    Guid Id,
    Guid ActorId,
    string? ActorName,
    string Action,
    string? FromValue,
    string? ToValue,
    string? Note,
    Guid BidId,
    string BidTitle,
    DateTime CreatedAt
);

public record InviteMemberDto(string Email, string Role, string? Department);

/// <summary>One pending invite (no <c>users</c> row yet) as the SPA's admin Teams page sees it.</summary>
public record PendingInviteDto(
    Guid Id,
    string Email,
    string Role,
    string? Department,
    Guid InvitedBy,
    string? InvitedByName,
    DateTime ExpiresAt,
    DateTime CreatedAt
);

/// <summary>Response for <c>POST /api/organizations/{id}/members</c>.</summary>
public record InviteMemberResultDto(
    /// <summary>Always <c>"invited"</c> — membership is never granted at invite time.
    /// Existing users confirm via the emailed accept page; new users register with the
    /// token. (<c>"added"</c> was the pre-confirmation-flow value and is no longer produced.)</summary>
    string Status,

    /// <summary>Legacy field from the instant-add flow; always <c>null</c> now.</summary>
    OrgMemberDto? Member,

    /// <summary>The email the invitation was sent to.</summary>
    string? InvitedEmail,

    /// <summary>When the invite token stops working.</summary>
    DateTime? ExpiresAt
);

/// <summary>
/// What the SPA's accept-invite page shows before the invitee decides. Fetched
/// anonymously — the single-use token in the link is the credential.
/// </summary>
public record InvitePreviewDto(
    string OrgName,
    string? OrgLogoUrl,
    string InviterName,
    string Role,
    /// <summary>The invited email (lowercased). The page uses it to detect a
    /// logged-in-as-someone-else mismatch client-side.</summary>
    string Email,
    DateTime ExpiresAt,
    /// <summary>True when a user account already exists for the invited email —
    /// the page routes to sign-in; false routes to signup with the token.</summary>
    bool InviteeHasAccount
);

/// <summary>Result of <c>POST /api/invites/accept</c> — the org the user just joined,
/// so the SPA can switch its active-org context to it.</summary>
public record AcceptInviteResultDto(Guid OrgId, string OrgName, string Role);

/// <summary>
/// One pending invite addressed to the logged-in user's email — powers the
/// onboarding "join your team" branch for social signups. Carries no token:
/// accept is by id, gated on the caller being authenticated as the invited email.
/// </summary>
public record MyInviteDto(
    Guid Id,
    string OrgName,
    string? OrgLogoUrl,
    string InviterName,
    string Role,
    DateTime ExpiresAt
);

/// <summary>Body for <c>POST /api/invites/accept</c> and <c>/decline</c>.</summary>
public record InviteTokenDto(string Token);

public record UpdateMemberDto(string? Role, string? Department, string? Status);

/// <summary>
/// Operator request to grant or revoke buyer status (<c>POST /internal/organizations/{id}/org-type</c>).
/// </summary>
/// <remarks>
/// Deliberately NOT part of <see cref="CreateOrgDto"/> or <see cref="UpdateOrgDto"/>. A buyer org can
/// publish notices on the public portal under a department's name, so becoming one is provisioned by
/// an operator after whatever verification happened offline — never claimed by the org itself.
///
/// <para><paramref name="VerificationNote"/> is free text recorded in the audit trail — a file
/// number, an email, whatever proved this is really the department it says it is.</para>
/// </remarks>
public record SetOrgTypeDto(
    string OrgType,
    string? EntityType = null,
    string? Ministry = null,
    string? Department = null,
    string? Office = null,
    string? ProcuringEntityCode = null,
    string? VerificationNote = null);

public record OrgTypeResultDto(
    Guid OrgId,
    string Name,
    string PreviousOrgType,
    string OrgType,
    string? EntityType,
    string? Ministry,
    string? Department,
    string? Office,
    string? ProcuringEntityCode);

// ── Buyer access requests (migration 0033) ───────────────────────────────────

/// <summary>
/// An owner/admin asking the platform to make their org a buyer. Self-serve to RAISE; an operator
/// still decides, because a buyer publishes under a department's name.
/// </summary>
public record RequestBuyerAccessDto(
    string Justification,
    string? EntityType = null,
    string? Ministry = null,
    string? Department = null,
    string? Office = null,
    string? ProcuringEntityCode = null);

/// <summary>The org's own view of its request — drives the Settings card's state.</summary>
public record BuyerRequestDto(
    Guid Id,
    Guid OrgId,
    string Status,
    string? EntityType,
    string? Ministry,
    string? Department,
    string? Office,
    string? ProcuringEntityCode,
    string Justification,
    string? DecisionNote,
    string RequesterName,
    DateTime? DecidedAt,
    DateTime CreatedAt);

/// <summary>The operator's queue row — the org, who asked, and what they claimed.</summary>
public record BuyerRequestQueueItemDto(
    Guid Id,
    Guid OrgId,
    string OrgName,
    string? OrgGstin,
    string Status,
    string? EntityType,
    string? Ministry,
    string? Department,
    string? Office,
    string? ProcuringEntityCode,
    string Justification,
    string RequesterName,
    string RequesterEmail,
    DateTime CreatedAt);

/// <summary>Operator's decision. <c>OrgType</c> defaults to <c>buyer</c>; an operator may grant
/// <c>both</c> for a PSU that also bids.</summary>
public record ApproveBuyerRequestDto(string? DecisionNote = null, string OrgType = "buyer");

public record RejectBuyerRequestDto(string DecisionNote);
