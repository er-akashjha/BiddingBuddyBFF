namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>The verified subset of an Apple identity token we trust after signature + claim checks.</summary>
public record AppleIdentity(string Sub, string? Email, bool EmailVerified);

/// <summary>
/// Verifies a "Sign in with Apple" identity token (JWT) against Apple's published JWKS:
/// RS256 signature, issuer <c>https://appleid.apple.com</c>, audience = our bundle id,
/// and lifetime. Throws <see cref="UnauthorizedAccessException"/> if anything fails.
/// </summary>
public interface IAppleTokenVerifier
{
    /// <summary>True when an Apple audience (bundle id) is configured — gates the endpoint.</summary>
    bool IsConfigured { get; }

    Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken ct = default);
}
