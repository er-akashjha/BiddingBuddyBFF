using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Verifies Microsoft Entra ID <c>id_token</c>s. Fetches the JWKS for the configured authority
/// (cached ~24 h) and validates signature / audience / lifetime / issuer / nonce.
/// </summary>
/// <remarks>
/// Three things differ from <see cref="AppleTokenVerifier"/>, and each is a trap:
/// <list type="number">
/// <item><b>The issuer is per-tenant.</b> Entra signs as
/// <c>https://login.microsoftonline.com/{tid}/v2.0</c>, so there is no single string to put in
/// <c>ValidIssuer</c> for a multitenant app. The temptation is <c>ValidateIssuer = false</c>, which
/// accepts tokens from any issuer whose key happens to be in the set. Instead we validate the issuer
/// *relative to the token's own <c>tid</c>* — see <see cref="ValidateIssuer"/>.</item>
/// <item><b>Personal accounts sign in under a well-known tenant.</b> The <c>/organizations</c>
/// authority should already exclude them; we reject <see cref="MsaConsumerTenant"/> explicitly anyway,
/// because "work accounts only" is a product decision and shouldn't rest on one config string.</item>
/// <item><b>The nonce must be checked.</b> We pin it in the signed state at authorization time and
/// assert it here, so an <c>id_token</c> minted for a different sign-in cannot be replayed into ours.</item>
/// </list>
/// </remarks>
public class MicrosoftTokenVerifier : IMicrosoftTokenVerifier
{
    /// <summary>The fixed tenant every personal Microsoft account authenticates under.</summary>
    private const string MsaConsumerTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    private const string DefaultAuthority = "https://login.microsoftonline.com/organizations";
    private static readonly TimeSpan JwksTtl = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MicrosoftTokenVerifier> _log;
    private readonly string[] _audiences;
    private readonly string _jwksUrl;

    private readonly SemaphoreSlim _jwksLock = new(1, 1);
    private IList<SecurityKey>? _keys;
    private DateTime _keysFetchedAtUtc = DateTime.MinValue;

    public MicrosoftTokenVerifier(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<MicrosoftTokenVerifier> log)
    {
        _httpFactory = httpFactory;
        _log = log;

        var ids = new List<string>();
        if (config["OAuth:Microsoft:ClientId"] is { Length: > 0 } single) ids.Add(single);
        _audiences = ids.Distinct(StringComparer.Ordinal).ToArray();

        var authority = (config["OAuth:Microsoft:Authority"] ?? DefaultAuthority).TrimEnd('/');
        _jwksUrl = $"{authority}/discovery/v2.0/keys";
    }

    public bool IsConfigured => _audiences.Length > 0;

    public async Task<MicrosoftIdentity> VerifyAsync(
        string idToken, string? expectedNonce = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Microsoft sign-in is not configured (OAuth:Microsoft:ClientId).");
        if (string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Missing Microsoft identity token.");

        var keys = await GetSigningKeysAsync(ct);

        // Keep the claim names Microsoft actually sent. By default this handler rewrites inbound
        // claim types through DefaultInboundClaimTypeMap, which turns `tid` into
        // http://schemas.microsoft.com/identity/claims/tenantid, `oid` into .../objectidentifier and
        // `sub` into the ClaimTypes.NameIdentifier URI — so every FindFirst("tid") below silently
        // returns null and a perfectly valid token reads as "carries no tenant id".
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            IssuerValidator = ValidateIssuer,
            ValidateAudience = true,
            ValidAudiences = _audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        System.Security.Claims.ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Microsoft identity token validation failed.");
            throw new UnauthorizedAccessException("Invalid Microsoft identity token.");
        }

        // Replay guard. Compared in fixed time out of habit rather than necessity — the nonce is not
        // a secret, but constant-time comparison of anything token-shaped costs nothing.
        if (expectedNonce is not null)
        {
            var actualNonce = principal.FindFirst("nonce")?.Value;
            if (actualNonce is null || !FixedTimeEquals(actualNonce, expectedNonce))
            {
                _log.LogWarning("Microsoft identity token nonce did not match the pinned state nonce.");
                throw new UnauthorizedAccessException("Invalid Microsoft identity token.");
            }
        }

        var tid = principal.FindFirst("tid")?.Value;
        if (string.IsNullOrWhiteSpace(tid))
            throw new UnauthorizedAccessException("Microsoft token carries no tenant id.");

        if (string.Equals(tid, MsaConsumerTenant, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("MS_PERSONAL_ACCOUNT");

        var oid = principal.FindFirst("oid")?.Value;
        if (string.IsNullOrWhiteSpace(oid))
            throw new UnauthorizedAccessException("Microsoft token carries no object id.");

        var email = ResolveEmail(
            principal.FindFirst("email")?.Value,
            principal.FindFirst("preferred_username")?.Value,
            principal.FindFirst("upn")?.Value);

        if (email is null)
            throw new InvalidOperationException("MS_NO_EMAIL");

        return new MicrosoftIdentity(oid, tid, email, principal.FindFirst("name")?.Value);
    }

    /// <summary>
    /// Accepts the issuer only when it is exactly <c>https://login.microsoftonline.com/{tid}/v2.0</c>
    /// for the tenant the token itself claims. That coupling is the whole check: an attacker who can
    /// mint a token for their own tenant gets a token whose <c>tid</c> is their tenant, which then
    /// fails the org binding in <c>AuthService</c> rather than impersonating someone else's directory.
    /// </summary>
    private static string ValidateIssuer(string issuer, SecurityToken token, TokenValidationParameters _)
    {
        var tid = (token as JwtSecurityToken)?.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
        if (string.IsNullOrWhiteSpace(tid))
            throw new SecurityTokenInvalidIssuerException("Token has no tid claim to validate the issuer against.");

        // Entra v2.0 issues under login.microsoftonline.com. sts.windows.net is the v1.0 issuer and is
        // not expected here — if a v1.0 token ever arrives, failing loudly is the right outcome.
        var expected = $"https://login.microsoftonline.com/{tid}/v2.0";
        if (!string.Equals(issuer, expected, StringComparison.OrdinalIgnoreCase))
            throw new SecurityTokenInvalidIssuerException(
                $"Issuer '{issuer}' does not match the token's own tenant.");

        return issuer;
    }

    /// <summary>
    /// First value that actually looks like an address wins. <c>preferred_username</c> and <c>upn</c>
    /// are usually the UPN, which is usually — but not always — an email; a UPN like
    /// <c>priya@acme.local</c> or a bare sAMAccountName has to be rejected rather than stored as an
    /// email we will later try to send to.
    /// </summary>
    internal static string? ResolveEmail(params string?[] candidates)
        => candidates.FirstOrDefault(LooksLikeEmail)?.Trim().ToLowerInvariant();

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Any(char.IsWhiteSpace)) return false;

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@')) return false;

        var domain = trimmed[(at + 1)..];
        var dot = domain.IndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

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
            var json = await http.GetStringAsync(_jwksUrl, ct);
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
