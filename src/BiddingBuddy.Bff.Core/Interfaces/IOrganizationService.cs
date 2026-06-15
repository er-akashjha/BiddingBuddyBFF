using BiddingBuddy.Bff.Core.DTOs.Orgs;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOrganizationService
{
    Task<OrgDetailDto> CreateAsync(Guid ownerId, CreateOrgDto dto, CancellationToken ct = default);
    Task<OrgDetailDto> GetAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<OrgDetailDto> UpdateAsync(Guid orgId, Guid userId, UpdateOrgDto dto, CancellationToken ct = default);
    Task DeactivateAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgMemberDto>> GetMembersAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>List pending invites for this org (one-token-per-row in <c>organization_invites</c>
    /// where <c>accepted_at IS NULL</c> and <c>expires_at &gt; now</c>).</summary>
    Task<IReadOnlyList<PendingInviteDto>> GetPendingInvitesAsync(Guid orgId, CancellationToken ct = default);

    Task<InviteMemberResultDto> InviteMemberAsync(Guid orgId, Guid invitedBy, InviteMemberDto dto, CancellationToken ct = default);

    /// <summary>Revoke a pending invite (sets <c>accepted_at = now</c> so the token can no longer be redeemed).</summary>
    Task RevokePendingInviteAsync(Guid orgId, Guid inviteId, Guid requestingUserId, CancellationToken ct = default);
    Task<OrgMemberDto> UpdateMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, UpdateMemberDto dto, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, CancellationToken ct = default);
}
