using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class TokenService(IConfiguration config) : ITokenService
{
    private readonly string _secret = config["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    private readonly string _issuer = config["Jwt:Issuer"] ?? "BiddingBuddyBFF";
    private readonly string _audience = config["Jwt:Audience"] ?? "BiddingBuddyClients";
    private readonly int _accessTokenMinutes = int.Parse(config["Jwt:AccessTokenExpiryMinutes"] ?? "15");
    private readonly int _refreshTokenDays = int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "30");

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Name),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string token, string hash) GenerateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = HashToken(token);
        return (token, hash);
    }

    public string GenerateStateToken(OAuthStateData data)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("return_url", data.ReturnUrl),
            new("nonce", Guid.NewGuid().ToString()),
        };
        // Mobile-only claims — absent for web flows, so the callback can tell them apart.
        if (data.Client is not null) claims.Add(new Claim("client", data.Client));
        if (data.CodeChallenge is not null) claims.Add(new Claim("code_challenge", data.CodeChallenge));
        if (data.RedirectUri is not null) claims.Add(new Claim("redirect_uri", data.RedirectUri));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),  // state expires in 10 min
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public bool TryValidateStateToken(string state, out OAuthStateData data)
    {
        data = new OAuthStateData("/");
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(state, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            data = new OAuthStateData(
                principal.FindFirst("return_url")?.Value ?? "/",
                principal.FindFirst("client")?.Value,
                principal.FindFirst("code_challenge")?.Value,
                principal.FindFirst("redirect_uri")?.Value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_refreshTokenDays);

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
