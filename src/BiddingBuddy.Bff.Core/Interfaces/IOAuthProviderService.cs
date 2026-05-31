namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOAuthProviderService
{
    string GetAuthorizationUrl(string provider, string state);
    Task<OAuthUserInfo> ExchangeCodeAsync(string provider, string code, CancellationToken ct = default);
    Task UnlinkAsync(Guid userId, string provider, CancellationToken ct = default);
}

public record OAuthUserInfo(
    string ProviderUserId,
    string Email,
    string Name,
    string? AvatarUrl,
    string? AccessToken,
    string? ProviderRefreshToken,
    DateTime? TokenExpiresAt
);
