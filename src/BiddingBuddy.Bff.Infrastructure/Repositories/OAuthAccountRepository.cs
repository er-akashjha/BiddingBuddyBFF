using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Repositories;

public class OAuthAccountRepository(BffDbContext db) : IOAuthAccountRepository
{
    public Task<OAuthAccount?> FindAsync(string provider, string providerUserId, CancellationToken ct = default)
        => db.OAuthAccounts.FirstOrDefaultAsync(
            a => a.Provider == provider && a.ProviderUserId == providerUserId, ct);

    public async Task<IReadOnlyList<OAuthAccount>> FindByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await db.OAuthAccounts.Where(a => a.UserId == userId).ToListAsync(ct);

    public async Task<OAuthAccount> CreateAsync(OAuthAccount account, CancellationToken ct = default)
    {
        db.OAuthAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task UpdateAsync(OAuthAccount account, CancellationToken ct = default)
    {
        db.OAuthAccounts.Update(account);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, string provider, CancellationToken ct = default)
    {
        await db.OAuthAccounts
            .Where(a => a.UserId == userId && a.Provider == provider)
            .ExecuteDeleteAsync(ct);
    }
}
