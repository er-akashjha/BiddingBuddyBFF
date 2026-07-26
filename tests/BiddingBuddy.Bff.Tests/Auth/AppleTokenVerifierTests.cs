using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Auth;

/// <summary>
/// Validation of Apple "Sign in with Apple" identity tokens.
///
/// <para>The load-bearing case here is claim *naming*, not signature validation.
/// <c>JwtSecurityTokenHandler</c> rewrites inbound claim types through
/// <c>DefaultInboundClaimTypeMap</c> unless told not to, which renames <c>sub</c> to the
/// <c>ClaimTypes.NameIdentifier</c> URI and <c>email</c> to the <c>ClaimTypes.Email</c> URI. A verifier
/// that reads <c>FindFirst("sub")</c> off a mapped principal therefore rejects a *perfectly valid*
/// token as having no subject — every sign-in fails, and it fails on the happy path, so nothing short
/// of a real token exercises it. <see cref="Verify_AcceptsAWellFormedToken"/> is that exercise.</para>
///
/// <para>The fixture mirrors <see cref="MicrosoftTokenVerifierTests"/>: one local RSA key serves as
/// both the JWKS the verifier fetches and the signer of every test token.</para>
/// </summary>
public sealed class AppleTokenVerifierTests
{
    private const string BundleId = "com.tendersagent.app";
    private const string ServicesId = "com.tendersagent.web";
    private const string AppleIssuer = "https://appleid.apple.com";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "apple-test-key" };

    // ── The claim map ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_AcceptsAWellFormedToken()
    {
        var identity = await Verifier().VerifyAsync(Token());

        Assert.Equal("001234.abcdef.5678", identity.Sub);
        Assert.Equal("priya@privaterelay.appleid.com", identity.Email);
        Assert.True(identity.EmailVerified);
    }

    [Fact]
    public async Task Verify_ReadsTheOidcClaimNamesAppleActuallySent()
    {
        // Pins the mapping fix directly rather than through its symptom. With the handler's default
        // inbound map left on, `sub` arrives as http://schemas.xmlsoap.org/ws/2005/05/identity/claims/
        // nameidentifier and `email` as .../emailaddress, so both lookups return null and the verifier
        // throws "Apple token has no subject" on a token that is entirely valid.
        var identity = await Verifier().VerifyAsync(Token(sub: "001111.deadbeef.2222"));

        Assert.Equal("001111.deadbeef.2222", identity.Sub);
        Assert.NotNull(identity.Email);
    }

    // ── email_verified ────────────────────────────────────────────────────────
    //
    // This value is the gate on adopting an existing account (AuthService.LinkOrCreateUserAsync
    // refuses to link an unverified identity to a user that already exists on that email), so a
    // misread in either direction is a security-relevant bug: false-when-true locks legitimate users
    // out of their own account, true-when-false hands the account to whoever asserted the address.
    //
    // Apple documents email_verified as "a string or Boolean value", and both forms occur in the
    // wild — the native iOS flow and the web flow do not agree. Both must read as verified.

    [Fact]
    public async Task Verify_ReadsEmailVerifiedWhenAppleSendsItAsAJsonBoolean()
    {
        var identity = await Verifier().VerifyAsync(Token(emailVerified: true, asBoolean: true));

        Assert.True(identity.EmailVerified);
    }

    [Fact]
    public async Task Verify_ReadsEmailVerifiedWhenAppleSendsItAsAJsonString()
    {
        var identity = await Verifier().VerifyAsync(Token(emailVerified: true, asBoolean: false));

        Assert.True(identity.EmailVerified);
    }

    [Fact]
    public async Task Verify_TreatsAnExplicitlyUnverifiedEmailAsUnverified()
    {
        Assert.False((await Verifier().VerifyAsync(Token(emailVerified: false, asBoolean: true))).EmailVerified);
        Assert.False((await Verifier().VerifyAsync(Token(emailVerified: false, asBoolean: false))).EmailVerified);
    }

    [Fact]
    public async Task Verify_TreatsAnAbsentEmailVerifiedAsUnverified()
    {
        // Fail closed: no assertion from Apple is not an assertion of verification.
        var identity = await Verifier().VerifyAsync(Token(emailVerified: null));

        Assert.False(identity.EmailVerified);
    }

    // ── email ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_AllowsAnAbsentEmail()
    {
        // Apple sends the email only on the FIRST authorization. Every later sign-in legitimately
        // omits it, and AuthService falls back to the address on the stored oauth_account — so the
        // verifier must return null here rather than reject the token.
        var identity = await Verifier().VerifyAsync(Token(email: null));

        Assert.Null(identity.Email);
        Assert.Equal("001234.abcdef.5678", identity.Sub);
    }

    // ── Signature / issuer / audience / lifetime ──────────────────────────────

    [Fact]
    public async Task Verify_RejectsAnIssuerOtherThanApple()
    {
        var token = Token(issuer: "https://accounts.google.com");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verifier().VerifyAsync(token));
    }

    [Fact]
    public async Task Verify_RejectsTheWrongAudience()
    {
        // An identity token minted for a different app is a valid, Apple-signed token. The audience
        // check is the only thing that stops it being replayed into ours.
        var token = Token(audience: "com.someone-else.app");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verifier().VerifyAsync(token));
    }

    [Fact]
    public async Task Verify_AcceptsTheWebServicesIdAudience()
    {
        // OAuth:Apple:ClientIds carries the web Services ID alongside the native bundle id.
        var identity = await Verifier().VerifyAsync(Token(audience: ServicesId));

        Assert.Equal("001234.abcdef.5678", identity.Sub);
    }

    [Fact]
    public async Task Verify_RejectsAnExpiredToken()
    {
        var token = Token(expires: DateTime.UtcNow.AddMinutes(-10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verifier().VerifyAsync(token));
    }

    [Fact]
    public async Task Verify_RejectsATokenSignedByAKeyOutsideApplesJwks()
    {
        var foreignKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "apple-test-key" };
        var token = Token(signingKey: foreignKey);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verifier().VerifyAsync(token));
    }

    [Fact]
    public async Task Verify_RejectsAMissingToken()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Verifier().VerifyAsync(""));
    }

    // ── Configuration ─────────────────────────────────────────────────────────

    [Fact]
    public void IsConfigured_IsFalseWithoutAClientId()
    {
        var verifier = new AppleTokenVerifier(
            HttpFactory(), new ConfigurationBuilder().Build(), NullLogger<AppleTokenVerifier>.Instance);

        Assert.False(verifier.IsConfigured);
    }

    [Fact]
    public async Task Verify_ThrowsWhenAppleSignInIsNotConfigured()
    {
        var verifier = new AppleTokenVerifier(
            HttpFactory(), new ConfigurationBuilder().Build(), NullLogger<AppleTokenVerifier>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync(Token()));
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static AppleTokenVerifier Verifier()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OAuth:Apple:ClientId"] = BundleId,
            ["OAuth:Apple:ClientIds:0"] = ServicesId,
        }).Build();

        return new AppleTokenVerifier(HttpFactory(), config, NullLogger<AppleTokenVerifier>.Instance);
    }

    /// <summary>An <c>IHttpClientFactory</c> whose every response is our single-key JWKS.</summary>
    private static IHttpClientFactory HttpFactory()
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(SigningKey);
        jwk.Use = "sig";
        jwk.Alg = "RS256";
        var jwks = $"{{\"keys\":[{System.Text.Json.JsonSerializer.Serialize(jwk)}]}}";

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(jwks),
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));
        return factory.Object;
    }

    private static string Token(
        string sub = "001234.abcdef.5678",
        string? email = "priya@privaterelay.appleid.com",
        bool? emailVerified = true,
        bool asBoolean = true,
        string issuer = AppleIssuer,
        string audience = BundleId,
        DateTime? expires = null,
        RsaSecurityKey? signingKey = null)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (email is not null) claims.Add(new Claim("email", email));

        // ClaimValueTypes.Boolean makes the handler serialize a JSON boolean; the default string
        // value type serializes a quoted string. Apple emits both across its flows.
        if (emailVerified is { } verified)
        {
            var raw = verified ? "true" : "false";
            claims.Add(asBoolean
                ? new Claim("email_verified", raw, ClaimValueTypes.Boolean)
                : new Claim("email_verified", raw));
        }

        // nbf is derived from exp rather than from now: an already-expired token still needs
        // nbf < exp or the constructor rejects it before the verifier ever sees it.
        var exp = expires ?? DateTime.UtcNow.AddMinutes(10);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: exp.AddMinutes(-11),
            expires: exp,
            signingCredentials: new SigningCredentials(
                signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
