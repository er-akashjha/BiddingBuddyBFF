namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>The verified subset of a Microsoft Entra ID token we trust after signature + claim checks.</summary>
/// <param name="ObjectId">
/// <c>oid</c> — the user's immutable directory object id. Used as <c>oauth_accounts.provider_user_id</c>
/// in preference to <c>sub</c>, which is pairwise per (app, user) and therefore means nothing to a tenant
/// admin trying to identify the account in a support conversation.
/// </param>
/// <param name="TenantId">
/// <c>tid</c> — the directory the user belongs to. This is what an organization binds itself to, and the
/// only thing that grants membership.
/// </param>
/// <param name="Email">
/// Resolved from <c>email</c>, then <c>preferred_username</c>, then <c>upn</c> — the first that looks like
/// an address. Entra omits <c>email</c> entirely in tenants that never set the user's mail attribute, so
/// the fallbacks are the common case, not the edge case.
/// </param>
public record MicrosoftIdentity(
    string ObjectId,
    string TenantId,
    string Email,
    string? Name);

/// <summary>
/// Verifies a Microsoft Entra <c>id_token</c> against the published JWKS: RS256 signature, audience =
/// our app registration, lifetime, per-tenant issuer, and the nonce we pinned at authorization time.
/// Throws <see cref="UnauthorizedAccessException"/> if anything fails.
/// </summary>
public interface IMicrosoftTokenVerifier
{
    /// <summary>True when an Entra client id is configured — gates the provider.</summary>
    bool IsConfigured { get; }

    /// <param name="expectedNonce">
    /// The nonce sent in the authorization request. When non-null the token's <c>nonce</c> must match it
    /// exactly; a token carrying a different (or absent) nonce is rejected as a replay.
    /// </param>
    Task<MicrosoftIdentity> VerifyAsync(
        string idToken, string? expectedNonce = null, CancellationToken ct = default);
}
