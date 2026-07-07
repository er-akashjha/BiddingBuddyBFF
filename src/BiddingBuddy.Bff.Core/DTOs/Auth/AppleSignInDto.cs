namespace BiddingBuddy.Bff.Core.DTOs.Auth;

/// <summary>
/// Native "Sign in with Apple" completion. The app sends the <c>identityToken</c> from
/// ASAuthorization; <c>fullName</c> is only present on the very first authorization
/// (Apple never sends it again), so it's optional.
/// </summary>
public record AppleSignInDto(string IdentityToken, string? FullName);

/// <summary>
/// Self-service account deletion (App Store 5.1.1(v) / Play policy). Password users must
/// re-enter their password; OAuth/Apple-only users pass <c>confirm=true</c> (the valid JWT
/// is the credential).
/// </summary>
public record DeleteAccountDto(string? Password, bool Confirm = false);
