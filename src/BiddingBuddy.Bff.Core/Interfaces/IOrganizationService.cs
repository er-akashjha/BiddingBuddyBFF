using BiddingBuddy.Bff.Core.DTOs.Orgs;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOrganizationService
{
    Task<OrgDetailDto> CreateAsync(Guid ownerId, CreateOrgDto dto, CancellationToken ct = default);
    Task<OrgDetailDto> GetAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<OrgDetailDto> UpdateAsync(Guid orgId, Guid userId, UpdateOrgDto dto, CancellationToken ct = default);
    Task DeactivateAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrgMemberDto>> GetMembersAsync(Guid orgId, CancellationToken ct = default);
    Task<OrgMemberDto> InviteMemberAsync(Guid orgId, Guid invitedBy, InviteMemberDto dto, CancellationToken ct = default);
    Task<OrgMemberDto> UpdateMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, UpdateMemberDto dto, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, CancellationToken ct = default);
}
