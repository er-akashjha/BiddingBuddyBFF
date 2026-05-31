using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOrganizationRepository
{
    Task<Organization?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Organization>> FindByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Organization> CreateAsync(Organization org, CancellationToken ct = default);
    Task UpdateAsync(Organization org, CancellationToken ct = default);
    Task<bool> IsUserMemberAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<string?> GetUserRoleAsync(Guid orgId, Guid userId, CancellationToken ct = default);
}
