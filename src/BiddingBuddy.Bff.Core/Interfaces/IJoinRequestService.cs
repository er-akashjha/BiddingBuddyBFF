using BiddingBuddy.Bff.Core.DTOs.Orgs;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// The "my company is already here, let me in" flow — the mirror of the invite flow in
/// <see cref="IOrganizationService"/>. Raised when signup is blocked as a duplicate.
///
/// <para>The requester-facing half runs without org context (they are not a member yet);
/// the decision half is org-scoped and restricted to owners and admins.</para>
/// </summary>
public interface IJoinRequestService
{
    /// <summary>
    /// Ask to join an existing org. Idempotent — an existing pending request is returned
    /// rather than duplicated. Throws <c>KeyNotFoundException</c> for an unknown or inactive
    /// org, and <c>InvalidOperationException("ALREADY_MEMBER")</c> if the caller is already in.
    /// </summary>
    Task<JoinRequestResultDto> RequestAsync(Guid userId, CreateJoinRequestDto dto, CancellationToken ct = default);

    /// <summary>The caller's own requests — live ones plus recent decisions, so the client can
    /// show "waiting for approval" and "your request was declined" without a second endpoint.</summary>
    Task<IReadOnlyList<MyJoinRequestDto>> GetMineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Withdraw one's own pending request. No-op if it was already decided.</summary>
    Task CancelAsync(Guid userId, Guid requestId, CancellationToken ct = default);

    /// <summary>Pending requests for an org, oldest first. Owner/admin only.</summary>
    Task<IReadOnlyList<OrgJoinRequestDto>> GetPendingForOrgAsync(Guid orgId, Guid requestingUserId, CancellationToken ct = default);

    /// <summary>Approve → create the membership with the approver's chosen role, and notify the
    /// requester. Owner/admin only. Throws <c>InvalidOperationException("INVALID_ROLE")</c> for a
    /// role outside the grantable set.</summary>
    Task<OrgMemberDto> ApproveAsync(Guid orgId, Guid requestId, Guid approverId, ApproveJoinRequestDto dto, CancellationToken ct = default);

    /// <summary>Decline the request and notify the requester. Owner/admin only.</summary>
    Task RejectAsync(Guid orgId, Guid requestId, Guid deciderId, CancellationToken ct = default);
}
