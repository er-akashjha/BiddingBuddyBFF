using System.IdentityModel.Tokens.Jwt;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Verifies Apple "Sign in with Apple" identity tokens. Fetches Apple's JWKS (cached ~24h)
/// and validates issuer / audience / signature / lifetime. Audiences come from
/// <c>OAuth:Apple:ClientId</c> (+ optional <c>OAuth:Apple:ClientIds</c> for a web Services ID).
/// </summary>
public class AppleTokenVerifier : IAppleTokenVerifier
{
    private const string Issuer = "https://appleid.apple.com";
    private const string JwksUrl = "https://appleid.apple.com/auth/keys";
    private static readonly TimeSpan JwksTtl = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AppleTokenVerifier> _log;
    private readonly string[] _audiences;

    private readonly SemaphoreSlim _jwksLock = new(1, 1);
    private IList<SecurityKey>? _keys;
    private DateTime _keysFetchedAtUtc = DateTime.MinValue;

    public AppleTokenVerifier(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<AppleTokenVerifier> log)
    {
        _httpFactory = httpFactory;
        _log = log;

        var ids = new List<string>();
        if (config["OAuth:Apple:ClientId"] is { Length: > 0 } single) ids.Add(single);
        if (config.GetSection("OAuth:Apple:ClientIds").Get<string[]>() is { } many) ids.AddRange(many);
        _audiences = ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    public bool IsConfigured => _audiences.Length > 0;

    public async Task<AppleIdentity> VerifyAsync(string identityToken, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Apple sign-in is not configured (OAuth:Apple:ClientId).");
        if (string.IsNullOrWhiteSpace(identityToken))
            throw new UnauthorizedAccessException("Missing Apple identity token.");

        var keys = await GetSigningKeysAsync(ct);

        var handler = new JwtSecurityTokenHandler();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudiences = _audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        try
        {
            var principal = handler.ValidateToken(identityToken, parameters, out _);
            var sub = principal.FindFirst("sub")?.Value
                      ?? throw new UnauthorizedAccessException("Apple token has no subject.");
            var email = principal.FindFirst("email")?.Value;
            var emailVerified = principal.FindFirst("email_verified")?.Value
                is "true" or "1";
            return new AppleIdentity(sub, email, emailVerified);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Apple identity token validation failed.");
            throw new UnauthorizedAccessException("Invalid Apple identity token.");
        }
    }

    private async Task<IList<SecurityKey>> GetSigningKeysAsync(CancellationToken ct)
    {
        if (_keys is not null && DateTime.UtcNow - _keysFetchedAtUtc < JwksTtl)
            return _keys;

        await _jwksLock.WaitAsync(ct);
        try
        {
            if (_keys is not null && DateTime.UtcNow - _keysFetchedAtUtc < JwksTtl)
                return _keys;

            var http = _httpFactory.CreateClient();
            var json = await http.GetStringAsync(JwksUrl, ct);
            var keys = new JsonWebKeySet(json).GetSigningKeys();
            _keys = keys;
            _keysFetchedAtUtc = DateTime.UtcNow;
            return keys;
        }
        finally
        {
            _jwksLock.Release();
        }
    }
}
