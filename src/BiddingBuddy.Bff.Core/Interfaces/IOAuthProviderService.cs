namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOAuthProviderService
{
    /// <param name="nonce">
    /// Echoed into the authorization request for providers whose <c>id_token</c> we validate
    /// (Microsoft). Ignored by providers we resolve via a userinfo call (Google, GitHub, Facebook).
    /// </param>
    /// <param name="loginHint">
    /// Pre-fills the provider's account picker with an email the user already typed. Purely
    /// cosmetic — identity is taken from the provider's response, never from this value.
    /// </param>
    string GetAuthorizationUrl(string provider, string state, string? nonce = null, string? loginHint = null);

    Task<OAuthUserInfo> ExchangeCodeAsync(
        string provider, string code, string? expectedNonce = null, CancellationToken ct = default);

    Task UnlinkAsync(Guid userId, string provider, CancellationToken ct = default);
}

/// <param name="EmailVerified">
/// Whether the provider actually asserts this address belongs to the person signing in. Load-bearing:
/// <c>AuthService.LinkOrCreateUserAsync</c> will only adopt a pre-existing account by email match when
/// this is true, because an unverified provider email is otherwise an account-takeover primitive.
/// Defaults to false so a new provider has to opt in deliberately.
/// </param>
/// <param name="TenantId">
/// The Entra directory (tenant) the user signed in from — <c>tid</c>. Only Microsoft sets it. This is
/// the ONLY evidence we accept for organization auto-join; an email domain is never sufficient.
/// </param>
public record OAuthUserInfo(
    string ProviderUserId,
    string Email,
    string Name,
    string? AvatarUrl,
    string? AccessToken,
    string? ProviderRefreshToken,
    DateTime? TokenExpiresAt,
    bool EmailVerified = false,
    string? TenantId = null
);
