using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task DeleteExpiredAsync(CancellationToken ct = default);
}
