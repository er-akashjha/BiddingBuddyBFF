using System.Security.Claims;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    (string token, string hash) GenerateRefreshToken();
    string GenerateStateToken(string returnUrl);
    bool TryValidateStateToken(string state, out string returnUrl);
}
