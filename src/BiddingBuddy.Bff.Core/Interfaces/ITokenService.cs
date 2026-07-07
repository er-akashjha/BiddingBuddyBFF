using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    (string token, string hash) GenerateRefreshToken();
    /// <summary>Signed short-lived CSRF state for the OAuth dance. Web flows carry only the
    /// return URL; mobile flows additionally pin the PKCE challenge + app redirect.</summary>
    string GenerateStateToken(OAuthStateData data);
    bool TryValidateStateToken(string state, out OAuthStateData data);
}
