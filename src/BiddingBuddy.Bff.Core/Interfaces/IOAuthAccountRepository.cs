using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOAuthAccountRepository
{
    Task<OAuthAccount?> FindAsync(string provider, string providerUserId, CancellationToken ct = default);
    Task<IReadOnlyList<OAuthAccount>> FindByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<OAuthAccount> CreateAsync(OAuthAccount account, CancellationToken ct = default);
    Task UpdateAsync(OAuthAccount account, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, string provider, CancellationToken ct = default);
}
