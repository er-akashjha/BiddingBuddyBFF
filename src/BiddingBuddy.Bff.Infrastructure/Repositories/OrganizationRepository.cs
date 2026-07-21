using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Repositories;

public class OrganizationRepository(BffDbContext db) : IOrganizationRepository
{
    public Task<Organization?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => db.Organizations
            .Include(o => o.Members)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Organization>> FindByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        // orgs the user owns OR is a member of
        var memberOrgIds = await db.OrgMembers
            .Where(m => m.UserId == userId && m.Status == "active")
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        return await db.Organizations
            .Where(o => memberOrgIds.Contains(o.Id) && o.IsActive)
            .ToListAsync(ct);
    }

    public async Task<Organization> CreateAsync(Organization org, CancellationToken ct = default)
    {
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
        return org;
    }

    public async Task UpdateAsync(Organization org, CancellationToken ct = default)
    {
        db.Organizations.Update(org);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMemberAsync(OrgMember member, CancellationToken ct = default)
    {
        db.OrgMembers.Add(member);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The gate <see cref="Api.Middleware.OrgContextMiddleware"/> uses to decide 403.
    /// </summary>
    /// <remarks>
    /// The <c>o.IsActive</c> term must stay in lockstep with <see cref="FindByUserIdAsync"/>,
    /// which backs <c>GET /api/auth/me</c>. These are the two answers to "which orgs can I
    /// use", and they used to disagree: this checked only the membership row, so a
    /// deactivated organization passed the gate while vanishing from the switcher. A client
    /// reconciling its cached org against <c>/api/auth/me</c> would then move the user off an
    /// org that in fact still worked — and the SPA now does exactly that on every 403.
    /// </remarks>
    public Task<bool> IsUserMemberAsync(Guid orgId, Guid userId, CancellationToken ct = default)
        => db.OrgMembers.AnyAsync(
            m => m.OrgId == orgId
              && m.UserId == userId
              && m.Status == "active"
              && m.Organization.IsActive, ct);

    public async Task<string?> GetUserRoleAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var member = await db.OrgMembers
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active", ct);
        return member?.Role;
    }
}
