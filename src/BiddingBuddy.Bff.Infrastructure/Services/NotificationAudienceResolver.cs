using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Resolves notification recipients from org membership. All reads are AsNoTracking —
/// callers share the scoped DbContext with <see cref="INotificationPublisher"/>, whose
/// SaveChanges must not pick up stray tracked entities.
/// </summary>
public class NotificationAudienceResolver(BffDbContext db) : INotificationAudienceResolver
{
    public async Task<IReadOnlyList<NotificationAudienceMember>> ByRolesAsync(
        Guid orgId, IReadOnlyCollection<string> roles, Guid? excludeUserId = null, CancellationToken ct = default)
    {
        if (roles.Count == 0) return [];

        var members = await db.OrgMembers
            .AsNoTracking()
            .Where(m => m.OrgId == orgId && m.Status == "active" && roles.Contains(m.Role))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Name, u.Email })
            .Distinct()
            .ToListAsync(ct);

        return members
            .Where(u => excludeUserId is null || u.Id != excludeUserId)
            .Select(u => new NotificationAudienceMember(u.Id, u.Name, u.Email))
            .ToList();
    }

    public async Task<NotificationAudienceMember?> ByUserAsync(Guid userId, CancellationToken ct = default)
        => await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new NotificationAudienceMember(u.Id, u.Name, u.Email))
            .FirstOrDefaultAsync(ct);
}
