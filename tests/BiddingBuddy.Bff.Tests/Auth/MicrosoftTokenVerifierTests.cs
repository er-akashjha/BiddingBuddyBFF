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
/// Validation of Microsoft Entra <c>id_token</c>s.
///
/// <para>The load-bearing case is the issuer. Entra is multitenant, so it signs as
/// <c>https://login.microsoftonline.com/{tid}/v2.0</c> — there is no single string to put in
/// <c>ValidIssuer</c>, and the tempting shortcut is <c>ValidateIssuer = false</c>, which accepts any
/// issuer whose signing key happens to be in the set. The verifier instead checks the issuer against
/// the token's OWN <c>tid</c>, and that coupling is what these tests pin.</para>
/// </summary>
public sealed class MicrosoftTokenVerifierTests
{
    private const string ClientId = "11111111-aaaa-bbbb-cccc-222222222222";
    private const string Tenant = "33333333-dddd-eeee-ffff-444444444444";

    // One key for the whole fixture: the JWKS the verifier fetches is built from it, so a token
    // signed here verifies unless a test deliberately breaks something else.
    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    // ── Email resolution ──────────────────────────────────────────────────────
    //
    // Entra omits the `email` claim entirely in tenants that never set the user's mail attribute,
    // which is common enough that the fallbacks are the normal path, not the edge case.

    [Fact]
    public void ResolveEmail_PrefersEmailThenPreferredUsernameThenUpn()
    {
        Assert.Equal("a@acme.com", MicrosoftTokenVerifier.ResolveEmail("a@acme.com", "b@acme.com", "c@acme.com"));
        Assert.Equal("b@acme.com", MicrosoftTokenVerifier.ResolveEmail(null, "b@acme.com", "c@acme.com"));
        Assert.Equal("c@acme.com", MicrosoftTokenVerifier.ResolveEmail(null, null, "c@acme.com"));
    }

    [Fact]
    public void ResolveEmail_SkipsCandidatesThatAreNotAddresses()
    {
        // A UPN is usually an email but is not required to be one. A bare sAMAccountName or an
        // internal-only suffix has to be skipped rather than stored as an address we later email.
        Assert.Equal("real@acme.com", MicrosoftTokenVerifier.ResolveEmail("priya", "real@acme.com"));
        Assert.Null(MicrosoftTokenVerifier.ResolveEmail("priya", "acme\\priya", null));
        Assert.Null(MicrosoftTokenVerifier.ResolveEmail("two words@acme.com"));
        Assert.Null(MicrosoftTokenVerifier.ResolveEmail("a@@acme.com"));
        Assert.Null(MicrosoftTokenVerifier.ResolveEmail("trailing@"));
    }

    [Fact]
    public void ResolveEmail_NormalisesCase()
    {
        // Users are looked up by email; a mixed-case claim must not create a second account.
        Assert.Equal("priya@acme.com", MicrosoftTokenVerifier.ResolveEmail("Priya@ACME.com"));
    }

    // ── Token validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_AcceptsAWellFormedToken()
    {
        var verifier = Verifier();
        var token = Token();

        var identity = await verifier.VerifyAsync(token, expectedNonce: "nonce-1");

        Assert.Equal("oid-priya", identity.ObjectId);
        Assert.Equal(Tenant, identity.TenantId);
        Assert.Equal("priya@acme.com", identity.Email);
    }

    [Fact]
    public async Task Verify_RejectsAnIssuerThatDisagreesWithTheTokensOwnTenant()
    {
        // The attack this stops: a token minted under a directory the attacker controls, relabelled
        // with a victim tenant's tid so it would auto-join the victim's workspace.
        var token = Token(issuerTenant: "99999999-9999-9999-9999-999999999999");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
    }

    [Fact]
    public async Task Verify_RejectsAMismatchedNonce()
    {
        var token = Token(nonce: "someone-elses-sign-in");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
    }

    [Fact]
    public async Task Verify_RejectsAMissingNonceWhenOneWasPinned()
    {
        var token = Token(nonce: null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
    }

    [Fact]
    public async Task Verify_RejectsTheWrongAudience()
    {
        var token = Token(audience: "some-other-app");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
    }

    [Fact]
    public async Task Verify_RejectsAnExpiredToken()
    {
        var token = Token(expires: DateTime.UtcNow.AddMinutes(-10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
    }

    [Fact]
    public async Task Verify_RejectsAPersonalMicrosoftAccount()
    {
        // The /organizations authority should already have turned this away. Rejecting it here too
        // means "work accounts only" does not rest on one config string being right.
        var token = Token(tenant: "9188040d-6c67-4c5b-b112-36a304b66dad");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
        Assert.Equal("MS_PERSONAL_ACCOUNT", ex.Message);
    }

    [Fact]
    public async Task Verify_ThrowsMsNoEmailWhenNoClaimLooksLikeAnAddress()
    {
        var token = Token(email: null, preferredUsername: "priya", upn: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Verifier().VerifyAsync(token, expectedNonce: "nonce-1"));
        Assert.Equal("MS_NO_EMAIL", ex.Message);
    }

    [Fact]
    public async Task Verify_FallsBackToPreferredUsernameWhenEmailIsAbsent()
    {
        var token = Token(email: null, preferredUsername: "priya@acme.com");

        var identity = await Verifier().VerifyAsync(token, expectedNonce: "nonce-1");

        Assert.Equal("priya@acme.com", identity.Email);
    }

    [Fact]
    public void IsConfigured_IsFalseWithoutAClientId()
    {
        var verifier = new MicrosoftTokenVerifier(
            HttpFactory(), new ConfigurationBuilder().Build(), NullLogger<MicrosoftTokenVerifier>.Instance);

        Assert.False(verifier.IsConfigured);
    }

    // ── fixture ───────────────────────────────────────────────────────────────

    private static MicrosoftTokenVerifier Verifier()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OAuth:Microsoft:ClientId"] = ClientId,
        }).Build();

        return new MicrosoftTokenVerifier(HttpFactory(), config, NullLogger<MicrosoftTokenVerifier>.Instance);
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
        string tenant = Tenant,
        string? issuerTenant = null,
        string audience = ClientId,
        string? nonce = "nonce-1",
        string? email = "priya@acme.com",
        string? preferredUsername = null,
        string? upn = null,
        DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new("oid", "oid-priya"),
            new("tid", tenant),
            new("name", "Priya Nair"),
        };
        if (nonce is not null) claims.Add(new Claim("nonce", nonce));
        if (email is not null) claims.Add(new Claim("email", email));
        if (preferredUsername is not null) claims.Add(new Claim("preferred_username", preferredUsername));
        if (upn is not null) claims.Add(new Claim("upn", upn));

        // nbf is derived from exp, not from now: an already-expired token still needs nbf < exp or
        // the constructor rejects it before the verifier ever sees it.
        var exp = expires ?? DateTime.UtcNow.AddMinutes(10);

        var token = new JwtSecurityToken(
            issuer: $"https://login.microsoftonline.com/{issuerTenant ?? tenant}/v2.0",
            audience: audience,
            claims: claims,
            notBefore: exp.AddMinutes(-11),
            expires: exp,
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
