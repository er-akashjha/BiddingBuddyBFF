using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Auth;

/// <summary>
/// Binding an organization to a Microsoft Entra directory, and the anonymous lookup that routes a
/// login box.
///
/// <para>The invariant under test throughout: <b>you can only bind the tenant you personally signed
/// in with.</b> A tenant GUID is not a secret — it is discoverable for any company with a public
/// Entra presence — so a bind endpoint that accepted one from the request body would be a way to
/// claim a competitor's directory and collect their staff on next sign-in.</para>
/// </summary>
public sealed class SsoBindingTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid OtherOrg = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();

    private static readonly Guid AcmeTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RivalTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static SsoService Service(BffDbContext db) => new(db, NullLogger<SsoService>.Instance);

    /// <summary>An org with an owner and a viewer. The owner signed in with Microsoft; the viewer did not.</summary>
    private static async Task<BffDbContext> SeededAsync(string dbName)
    {
        var db = Db(dbName);

        db.Users.AddRange(
            new User { Id = Owner,  Name = "Priya Nair", Email = "priya@acme.example" },
            new User { Id = Viewer, Name = "Sam Viewer", Email = "sam@acme.example" });

        db.Organizations.AddRange(
            new Organization { Id = Org,      Name = "Acme Supplies Pvt Ltd", OwnedBy = Owner, IsActive = true },
            new Organization { Id = OtherOrg, Name = "Rival Traders",          OwnedBy = Owner, IsActive = true });

        db.OrgMembers.AddRange(
            new OrgMember { Id = Guid.NewGuid(), OrgId = Org, UserId = Owner,  Role = "owner",  Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = Org, UserId = Viewer, Role = "viewer", Status = "active" });

        // Only the owner has a Microsoft identity, and it belongs to AcmeTenant.
        db.OAuthAccounts.Add(new OAuthAccount
        {
            Id = Guid.NewGuid(), UserId = Owner, Provider = "microsoft",
            ProviderUserId = "oid-priya", Email = "priya@acme.example", TenantId = AcmeTenant,
        });

        await db.SaveChangesAsync();
        return db;
    }

    // ── Binding ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bind_UsesTheCallersOwnTenant()
    {
        using var db = await SeededAsync(nameof(Bind_UsesTheCallersOwnTenant));

        var result = await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        Assert.Equal(AcmeTenant, result.EntraTenantId);
        Assert.Equal(AcmeTenant, (await db.Organizations.FindAsync(Org))!.EntraTenantId);
    }

    [Fact]
    public async Task Bind_WithoutAMicrosoftIdentity_IsRefused()
    {
        using var db = await SeededAsync(nameof(Bind_WithoutAMicrosoftIdentity_IsRefused));

        // Promote the viewer so this fails on the missing proof, not on the role — otherwise the
        // test would pass for the wrong reason.
        var member = await db.OrgMembers.FirstAsync(m => m.UserId == Viewer);
        member.Role = "admin";
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).BindEntraTenantAsync(Org, Viewer, new BindEntraTenantDto()));

        Assert.Equal("NO_MICROSOFT_IDENTITY", ex.Message);
        Assert.Null((await db.Organizations.FindAsync(Org))!.EntraTenantId);
    }

    [Fact]
    public async Task Bind_CannotClaimATenantAlreadyOwnedByAnotherWorkspace()
    {
        using var db = await SeededAsync(nameof(Bind_CannotClaimATenantAlreadyOwnedByAnotherWorkspace));

        // Another workspace already owns AcmeTenant — exactly the collision the partial unique index
        // exists for. In-memory EF does not enforce that index, which is precisely why the service
        // checks explicitly rather than relying on the database to say no.
        var other = await db.Organizations.FindAsync(OtherOrg);
        other!.EntraTenantId = AcmeTenant;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto()));

        Assert.Equal("TENANT_ALREADY_BOUND", ex.Message);
    }

    [Fact]
    public async Task Bind_RequiresOwnerOrAdmin()
    {
        using var db = await SeededAsync(nameof(Bind_RequiresOwnerOrAdmin));

        // Give the viewer a Microsoft identity so the ONLY thing standing in the way is their role.
        db.OAuthAccounts.Add(new OAuthAccount
        {
            Id = Guid.NewGuid(), UserId = Viewer, Provider = "microsoft",
            ProviderUserId = "oid-sam", Email = "sam@acme.example", TenantId = AcmeTenant,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).BindEntraTenantAsync(Org, Viewer, new BindEntraTenantDto()));
    }

    [Fact]
    public async Task Bind_SeedsTheRoutingDomainFromTheBindingAdmin()
    {
        using var db = await SeededAsync(nameof(Bind_SeedsTheRoutingDomainFromTheBindingAdmin));

        var result = await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        // The admin's own address is in the directory being bound, so its domain is one Microsoft
        // already made that tenant prove by DNS. That is what lets routing skip a verification flow.
        Assert.Contains("acme.example", result.Domains);
    }

    [Fact]
    public async Task Bind_FallsBackToViewerForAnUnknownRole()
    {
        using var db = await SeededAsync(nameof(Bind_FallsBackToViewerForAnUnknownRole));

        var result = await Service(db).BindEntraTenantAsync(
            Org, Owner, new BindEntraTenantDto(DefaultRole: "superuser"));

        // Writing it through would hit the CHECK constraint and surface as a 500. Failing closed to
        // the weakest role is both a better error and the safer default.
        Assert.Equal("viewer", result.DefaultRole);
    }

    [Fact]
    public async Task Bind_AcceptsAKnownRole()
    {
        using var db = await SeededAsync(nameof(Bind_AcceptsAKnownRole));

        var result = await Service(db).BindEntraTenantAsync(
            Org, Owner, new BindEntraTenantDto(DefaultRole: "bid_manager"));

        Assert.Equal("bid_manager", result.DefaultRole);
    }

    // ── Unbinding ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unbind_ClearsRoutingButKeepsMembers()
    {
        using var db = await SeededAsync(nameof(Unbind_ClearsRoutingButKeepsMembers));
        await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        var result = await Service(db).UnbindEntraTenantAsync(Org, Owner);

        Assert.Null(result.EntraTenantId);
        Assert.Empty(result.Domains);

        // Disconnecting SSO stops future auto-joins. It is not a way to mass-remove the people who
        // already work here.
        Assert.Equal(2, await db.OrgMembers.CountAsync(m => m.OrgId == Org));
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lookup_RoutesAKnownDomainToMicrosoft()
    {
        using var db = await SeededAsync(nameof(Lookup_RoutesAKnownDomainToMicrosoft));
        await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        var result = await Service(db).LookupByEmailAsync("someone.else@acme.example");

        Assert.Equal("microsoft", result.Provider);
    }

    [Fact]
    public async Task Lookup_LeaksNothingBeyondTheProvider()
    {
        using var db = await SeededAsync(nameof(Lookup_LeaksNothingBeyondTheProvider));
        await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        var result = await Service(db).LookupByEmailAsync("someone.else@acme.example");

        // This endpoint is anonymous. If it carried the org name or id, it would be a directory of
        // our customers queryable by any domain a caller cares to guess. The whole DTO is one field
        // on purpose — this test fails the moment somebody widens it.
        Assert.Single(typeof(SsoLookupDto).GetProperties(), p => p.Name == nameof(SsoLookupDto.Provider));
        Assert.Single(typeof(SsoLookupDto).GetProperties());
    }

    [Theory]
    [InlineData("nobody@unknown.example")]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Lookup_AnswersNullWithoutThrowing(string? email)
    {
        using var db = await SeededAsync($"{nameof(Lookup_AnswersNullWithoutThrowing)}_{email ?? "null"}");
        await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        var result = await Service(db).LookupByEmailAsync(email);

        // Same answer for "unknown domain" and "garbage input", and never an exception: the login
        // page must degrade to the password box rather than break.
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task Lookup_StopsRoutingOnceTheOrgIsDeactivated()
    {
        using var db = await SeededAsync(nameof(Lookup_StopsRoutingOnceTheOrgIsDeactivated));
        await Service(db).BindEntraTenantAsync(Org, Owner, new BindEntraTenantDto());

        var org = await db.Organizations.FindAsync(Org);
        org!.IsActive = false;
        await db.SaveChangesAsync();

        // Otherwise a deactivated workspace leaves its former staff bouncing to an IdP that can no
        // longer place them anywhere — a login page they can neither use nor escape.
        Assert.Null((await Service(db).LookupByEmailAsync("priya@acme.example")).Provider);
    }

    [Fact]
    public void ExtractDomain_RejectsAnythingThatIsNotOneAddress()
    {
        Assert.Equal("acme.example", SsoService.ExtractDomain("Priya@ACME.example"));
        Assert.Equal("acme.co.uk",   SsoService.ExtractDomain("a@acme.co.uk"));
        Assert.Null(SsoService.ExtractDomain("no-at-sign"));
        Assert.Null(SsoService.ExtractDomain("trailing@"));
        Assert.Null(SsoService.ExtractDomain("@leading.com"));
        Assert.Null(SsoService.ExtractDomain("two words@acme.com"));
        // A bare hostname is not a routable mail domain and must not become one.
        Assert.Null(SsoService.ExtractDomain("user@localhost"));
    }
}
