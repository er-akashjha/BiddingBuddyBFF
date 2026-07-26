using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Repositories;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Auth;

/// <summary>
/// What happens when an external identity arrives: whether it may adopt an account that already
/// exists on the same email, and whether it walks into an organization.
///
/// <para>Two invariants, and they are separate on purpose:</para>
/// <list type="bullet">
/// <item><b>Adoption requires a verified email.</b> Creating a new account on an unverified address
/// is harmless. Attaching to an EXISTING one is a privilege transfer, so the provider has to
/// actually vouch for the address — otherwise any provider that lets a user type an unverified email
/// is a way to walk into someone else's account.</item>
/// <item><b>Auto-join requires a tenant match, never a domain match.</b> The tid arrives inside a
/// signature-verified id_token. The email domain is user-visible routing data and grants nothing.</item>
/// </list>
/// </summary>
public sealed class OAuthLinkAndAutoJoinTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid AcmeTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RivalTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>
    /// An org owned by Priya. <paramref name="boundTenant"/> is the Entra directory it has connected,
    /// if any; <paramref name="ssoRole"/> is what an auto-join grants.
    /// </summary>
    private static async Task<BffDbContext> SeededAsync(
        string dbName, Guid? boundTenant = null, string ssoRole = "viewer")
    {
        var db = Db(dbName);

        db.Users.Add(new User { Id = Owner, Name = "Priya Nair", Email = "priya@acme.example", IsActive = true });
        db.Organizations.Add(new Organization
        {
            Id = Org, Name = "Acme Supplies Pvt Ltd", OwnedBy = Owner, IsActive = true,
            EntraTenantId = boundTenant, SsoDefaultRole = ssoRole,
        });
        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = Org, UserId = Owner, Role = "owner", Status = "active",
        });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>
    /// A real AuthService over the in-memory DB, with the OAuth provider faked to return
    /// <paramref name="info"/> for any code. Repositories are the real ones — they are thin over the
    /// DbContext, and mocking them would only test the mocks.
    /// </summary>
    private static AuthService Service(BffDbContext db, OAuthUserInfo info)
    {
        var provider = new Mock<IOAuthProviderService>();
        provider
            .Setup(p => p.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(info);

        return new AuthService(
            new UserRepository(db),
            new OAuthAccountRepository(db),
            new RefreshTokenRepository(db),
            new OrganizationRepository(db),
            provider.Object,
            Mock.Of<IAppleTokenVerifier>(),
            new TokenService(JwtConfig()),
            Mock.Of<INotificationPublisher>(),
            db,
            JwtConfig(),
            NullLogger<AuthService>.Instance);
    }

    private static IConfiguration JwtConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "test-secret-that-is-at-least-32-characters-long",
        }).Build();

    private static OAuthUserInfo Info(
        string email = "sam@acme.example",
        string providerUserId = "oid-sam",
        bool emailVerified = true,
        Guid? tenantId = null) =>
        new(providerUserId, email, "Sam Sharma", null, "at", null, DateTime.UtcNow.AddHours(1),
            EmailVerified: emailVerified, TenantId: tenantId?.ToString());

    // ── The link gate ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UnverifiedEmail_CannotAdoptAnExistingAccount()
    {
        using var db = await SeededAsync(nameof(UnverifiedEmail_CannotAdoptAnExistingAccount));

        // Priya already exists. A provider that will not vouch for her address must not be able to
        // attach itself to her account — that is a login as Priya.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db, Info(email: "priya@acme.example", emailVerified: false))
                .HandleOAuthCallbackAsync("facebook", "code"));

        Assert.Equal("EMAIL_LINK_UNVERIFIED", ex.Message);
        Assert.False(await db.OAuthAccounts.AnyAsync(a => a.UserId == Owner));
    }

    [Fact]
    public async Task VerifiedEmail_AdoptsTheExistingAccount()
    {
        using var db = await SeededAsync(nameof(VerifiedEmail_AdoptsTheExistingAccount));

        await Service(db, Info(email: "priya@acme.example", emailVerified: true))
            .HandleOAuthCallbackAsync("microsoft", "code");

        // Same user, now with a second provider linked — not a duplicate account.
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.True(await db.OAuthAccounts.AnyAsync(a => a.UserId == Owner && a.Provider == "microsoft"));
    }

    [Fact]
    public async Task UnverifiedEmail_MayStillCreateABrandNewAccount()
    {
        using var db = await SeededAsync(nameof(UnverifiedEmail_MayStillCreateABrandNewAccount));

        var tokens = await Service(db, Info(email: "newcomer@elsewhere.example", emailVerified: false))
            .HandleOAuthCallbackAsync("facebook", "code");

        // Nothing is being taken over here, so the gate must not fire. Blocking this would break
        // Facebook signup outright.
        Assert.True(tokens.IsNewUser);
        Assert.True(await db.Users.AnyAsync(u => u.Email == "newcomer@elsewhere.example"));
    }

    [Fact]
    public async Task ReturningIdentity_IsUnaffectedByTheGate()
    {
        using var db = await SeededAsync(nameof(ReturningIdentity_IsUnaffectedByTheGate));
        db.OAuthAccounts.Add(new OAuthAccount
        {
            Id = Guid.NewGuid(), UserId = Owner, Provider = "facebook",
            ProviderUserId = "fb-priya", Email = "priya@acme.example",
        });
        await db.SaveChangesAsync();

        // The (provider, provider_user_id) pair already resolves to a user, so no adoption is
        // happening and no verification is needed. Otherwise every existing Facebook user would be
        // locked out by this change.
        var tokens = await Service(db, Info(email: "priya@acme.example", providerUserId: "fb-priya", emailVerified: false))
            .HandleOAuthCallbackAsync("facebook", "code");

        Assert.False(tokens.IsNewUser);
    }

    // ── Auto-join ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchingTenant_JoinsTheOrgAtTheConfiguredRole()
    {
        using var db = await SeededAsync(
            nameof(MatchingTenant_JoinsTheOrgAtTheConfiguredRole), AcmeTenant, ssoRole: "bid_manager");

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "sam@acme.example");
        var member = await db.OrgMembers.FirstOrDefaultAsync(m => m.OrgId == Org && m.UserId == user.Id);

        Assert.NotNull(member);
        Assert.Equal("bid_manager", member!.Role);
        Assert.Equal("active", member.Status);
    }

    [Fact]
    public async Task NonMatchingTenant_JoinsNothing()
    {
        using var db = await SeededAsync(nameof(NonMatchingTenant_JoinsNothing), AcmeTenant);

        // Same email domain as the bound org, different directory. If membership ever keyed off the
        // domain instead of the tid, this test is what would catch it.
        await Service(db, Info(email: "mallory@acme.example", tenantId: RivalTenant))
            .HandleOAuthCallbackAsync("microsoft", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "mallory@acme.example");
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == user.Id));
    }

    [Fact]
    public async Task NoBoundTenant_JoinsNothing()
    {
        using var db = await SeededAsync(nameof(NoBoundTenant_JoinsNothing));

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "sam@acme.example");
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == user.Id));
    }

    [Fact]
    public async Task UnknownSsoRole_FallsBackToViewer()
    {
        using var db = await SeededAsync(
            nameof(UnknownSsoRole_FallsBackToViewer), AcmeTenant, ssoRole: "superuser");

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "sam@acme.example");
        var member = await db.OrgMembers.FirstAsync(m => m.UserId == user.Id);

        // A role could be retired from the vocabulary while rows still carry it. Granting an
        // unrecognised role is worse than granting the weakest known one.
        Assert.Equal("viewer", member.Role);
    }

    [Fact]
    public async Task InactiveOrg_JoinsNothing()
    {
        using var db = await SeededAsync(nameof(InactiveOrg_JoinsNothing), AcmeTenant);
        var org = await db.Organizations.FindAsync(Org);
        org!.IsActive = false;
        await db.SaveChangesAsync();

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "sam@acme.example");
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == user.Id));
    }

    [Fact]
    public async Task ExistingMember_IsNotJoinedTwice()
    {
        using var db = await SeededAsync(nameof(ExistingMember_IsNotJoinedTwice), AcmeTenant);

        var info = Info(email: "priya@acme.example", tenantId: AcmeTenant);
        await Service(db, info).HandleOAuthCallbackAsync("microsoft", "code");
        await Service(db, info).HandleOAuthCallbackAsync("microsoft", "code");

        Assert.Equal(1, await db.OrgMembers.CountAsync(m => m.OrgId == Org && m.UserId == Owner));
    }

    [Fact]
    public async Task TenantIdIsRecordedOnTheOAuthAccount()
    {
        using var db = await SeededAsync(nameof(TenantIdIsRecordedOnTheOAuthAccount), AcmeTenant);

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        // This is the row SsoService reads to decide whether the caller may bind a tenant. Without
        // it, nobody could ever connect their directory.
        var account = await db.OAuthAccounts.FirstAsync(a => a.Provider == "microsoft");
        Assert.Equal(AcmeTenant, account.TenantId);
    }

    [Fact]
    public async Task SignInRecordsTheRoutingDomain()
    {
        using var db = await SeededAsync(nameof(SignInRecordsTheRoutingDomain), AcmeTenant);

        await Service(db, Info(tenantId: AcmeTenant)).HandleOAuthCallbackAsync("microsoft", "code");

        Assert.True(await db.OrgSsoDomains.AnyAsync(d => d.Domain == "acme.example" && d.OrgId == Org));
    }

    [Fact]
    public async Task ConsumerDomainIsNeverRecordedForRouting()
    {
        using var db = await SeededAsync(nameof(ConsumerDomainIsNeverRecordedForRouting), AcmeTenant);

        // The /organizations authority should make this unreachable. If it ever is reachable,
        // claiming gmail.com would route every Gmail user on the platform to this one customer.
        await Service(db, Info(email: "someone@gmail.com", tenantId: AcmeTenant))
            .HandleOAuthCallbackAsync("microsoft", "code");

        Assert.False(await db.OrgSsoDomains.AnyAsync(d => d.Domain == "gmail.com"));
    }

    [Fact]
    public async Task NonMicrosoftProvider_NeverAutoJoins()
    {
        using var db = await SeededAsync(nameof(NonMicrosoftProvider_NeverAutoJoins), AcmeTenant);

        // Google carries no tenant, so there is nothing to match and nothing to grant — the SSO path
        // must stay inert for every provider that cannot prove a directory.
        await Service(db, Info(email: "sam@acme.example", tenantId: null))
            .HandleOAuthCallbackAsync("google", "code");

        var user = await db.Users.FirstAsync(u => u.Email == "sam@acme.example");
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == user.Id));
    }
}
