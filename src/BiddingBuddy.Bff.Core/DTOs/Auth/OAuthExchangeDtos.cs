namespace BiddingBuddy.Bff.Core.DTOs.Auth;

/// <summary>
/// Claims carried through the OAuth dance inside the signed state JWT.
/// Web flows only set <see cref="ReturnUrl"/>; native apps additionally pin
/// <c>client=mobile</c>, their PKCE S256 challenge, and the allowlisted
/// redirect the callback should bounce back to.
/// </summary>
public record OAuthStateData(
    string ReturnUrl,
    string? Client = null,
    string? CodeChallenge = null,
    string? RedirectUri = null);

/// <summary>Body for <c>POST /api/auth/oauth/exchange</c> — redeem a one-time mobile OAuth code.</summary>
public record OAuthExchangeRequestDto(string Code, string CodeVerifier);

/// <summary>Outcome of a mobile OAuth callback: the one-time code handed back to the app.</summary>
public record MobileOAuthCodeDto(string Code, bool IsNewUser);
