using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Orgs;

/// <summary>
/// <c>POST /api/organizations</c> used to insert unconditionally, so the second person from a
/// company to sign up silently became the owner of an empty parallel workspace — same legal
/// entity, disjoint bids and documents, neither side ever seeing the other.
///
/// <para>The two signals have deliberately different strengths and the difference is the whole
/// design: a shared GSTIN is conclusive and cannot be overridden, a shared name is a coincidence
/// that the user is allowed to overrule. Getting either strength wrong is a silent failure —
/// too weak and duplicates keep appearing, too strong and a legitimately distinct company is
/// permanently locked out of signing up.</para>
/// </summary>
public sealed class OrgDuplicateDetectionTests
{
    private static readonly Guid Existing = Guid.NewGuid();
    private static readonly Guid Newcomer = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static OrganizationService Service(BffDbContext db) =>
        new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    private static CreateOrgDto NewOrg(
        string name, string? gstin = null, bool allowDuplicateName = false) =>
        new(name, null, gstin, null, null, null, null, null, null, null, null,
            null, null, null, allowDuplicateName);

    /// <summary>An org already in the database, plus its owner's membership row.</summary>
    private static async Task<BffDbContext> SeededAsync(
        string dbName, string name = "Acme Supplies Pvt Ltd", string? gstin = null, bool isActive = true)
    {
        var db = Db(dbName);
        db.Organizations.Add(new Organization
        {
            Id = Existing, Name = name, Gstin = gstin, City = "Pune",
            OwnedBy = Guid.NewGuid(), IsActive = isActive,
        });
        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = Existing, UserId = Guid.NewGuid(),
            Role = "owner", Status = "active",
        });
        await db.SaveChangesAsync();
        return db;
    }

    // ── GSTIN: the hard signal ────────────────────────────────────────────────

    [Fact]
    public async Task SameGstin_IsRefused()
    {
        using var db = await SeededAsync(nameof(SameGstin_IsRefused), gstin: "27AABCU9603R1ZX");

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg("Totally Different Name", "27AABCU9603R1ZX")));

        Assert.Equal("gstin", ex.Conflict.Match);
        Assert.Equal(Existing, ex.Conflict.Org.Id);
        // Nothing was written. A refusal that still leaves a row behind would be the original
        // bug wearing an error message.
        Assert.Single(await db.Organizations.ToListAsync());
    }

    [Theory]
    [InlineData("27aabcu9603r1zx")]      // lowercase
    [InlineData("27 AABCU 9603 R1ZX")]   // grouped with spaces, as printed on invoices
    [InlineData("  27AABCU9603R1ZX  ")]  // pasted with padding
    public async Task GstinMatch_IgnoresCaseAndWhitespace(string typed)
    {
        using var db = await SeededAsync($"gstin_norm_{typed.Trim()}", gstin: "27AABCU9603R1ZX");

        // Users paste GSTINs off invoices and portals, where they are routinely spaced and
        // cased differently. Comparing them raw would let the same registration through
        // under three spellings and defeat the check entirely.
        await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg("Anything", typed)));
    }

    [Fact]
    public async Task GstinMatch_CannotBeOverridden()
    {
        using var db = await SeededAsync(nameof(GstinMatch_CannotBeOverridden), gstin: "27AABCU9603R1ZX");

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer,
                NewOrg("Different Name", "27AABCU9603R1ZX", allowDuplicateName: true)));

        // AllowDuplicateName is consent to a *name* coincidence. One GSTIN is one legal entity,
        // so the flag must not become a bypass for the signal that is actually conclusive.
        Assert.Equal("gstin", ex.Conflict.Match);
        Assert.False(ex.Conflict.CanOverride);
    }

    [Fact]
    public async Task DifferentGstin_IsAllowed()
    {
        using var db = await SeededAsync(nameof(DifferentGstin_IsAllowed), gstin: "27AABCU9603R1ZX");

        await Service(db).CreateAsync(Newcomer, NewOrg("Beta Traders", "29AAGCB1286Q1ZL"));

        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    // ── Name: the soft signal ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Acme Supplies Private Limited")]
    [InlineData("ACME SUPPLIES PVT. LTD.")]
    [InlineData("acme supplies")]
    [InlineData("Acme  Supplies,  Pvt Ltd")]
    public async Task EquivalentName_IsWarnedAbout(string typed)
    {
        using var db = await SeededAsync($"name_{typed.GetHashCode()}");

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg(typed)));

        // Legal-form suffixes carry no distinguishing information — every second Indian SME
        // ends in some spelling of "private limited", and which spelling was typed says
        // nothing about whether it is the same company.
        Assert.Equal("name", ex.Conflict.Match);
        Assert.True(ex.Conflict.CanOverride);
    }

    [Fact]
    public async Task NameMatch_CanBeOverridden()
    {
        using var db = await SeededAsync(nameof(NameMatch_CanBeOverridden));

        await Service(db).CreateAsync(Newcomer, NewOrg("Acme Supplies", allowDuplicateName: true));

        // Unrelated firms genuinely share names. Once the user has been shown the existing org
        // and said "not us", refusing again would strand a real company with no way to sign up.
        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task UnrelatedName_IsAllowed()
    {
        using var db = await SeededAsync(nameof(UnrelatedName_IsAllowed));

        await Service(db).CreateAsync(Newcomer, NewOrg("Zenith Infrastructure"));

        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task SharedFirstWord_IsNotAMatch()
    {
        using var db = await SeededAsync(nameof(SharedFirstWord_IsNotAMatch));

        // "Acme Supplies" and "Acme Logistics" share the SQL prefix, so both arrive as
        // candidates. The normalized comparison in C# is what separates them — without it the
        // prefix query alone would block every company whose name starts with a common word.
        await Service(db).CreateAsync(Newcomer, NewOrg("Acme Logistics"));

        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    // ── Scope of the check ────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivatedOrg_DoesNotBlock()
    {
        using var db = await SeededAsync(nameof(DeactivatedOrg_DoesNotBlock),
            gstin: "27AABCU9603R1ZX", isActive: false);

        // A company that closed its workspace must be able to start again. Blocking on a
        // deactivated row would lock them out permanently with no self-serve way back.
        await Service(db).CreateAsync(Newcomer, NewOrg("Acme Supplies Pvt Ltd", "27AABCU9603R1ZX"));

        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task NoGstinAndNoNameMatch_CreatesNormally()
    {
        using var db = Db(nameof(NoGstinAndNoNameMatch_CreatesNormally));

        var org = await Service(db).CreateAsync(Newcomer, NewOrg("First Company Ever"));

        Assert.Equal("First Company Ever", org.Name);
        Assert.Equal("owner", org.UserRole);
    }

    [Fact]
    public async Task ConflictPayload_CarriesWhatTheCardNeeds()
    {
        using var db = await SeededAsync(nameof(ConflictPayload_CarriesWhatTheCardNeeds),
            gstin: "27AABCU9603R1ZX");

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg("Acme", "27AABCU9603R1ZX")));

        // The card has to be recognisable as *your* employer, which takes a name, a city and a
        // sense of size. Anything more — an admin's name or email — would hand whoever guessed
        // a (publicly printed) GSTIN a named target, so the DTO has no field for one.
        Assert.Equal("ORG_EXISTS", ex.Conflict.Error);
        Assert.Equal("Acme Supplies Pvt Ltd", ex.Conflict.Org.Name);
        Assert.Equal("Pune", ex.Conflict.Org.City);
        Assert.Equal(1, ex.Conflict.Org.MemberCount);
        Assert.Null(ex.Conflict.ExistingRequest);
    }

    [Fact]
    public async Task ConflictPayload_SurfacesTheCallersLiveRequest()
    {
        using var db = await SeededAsync(nameof(ConflictPayload_SurfacesTheCallersLiveRequest),
            gstin: "27AABCU9603R1ZX");
        db.OrgJoinRequests.Add(new OrgJoinRequest
        {
            Id = Guid.NewGuid(), OrgId = Existing, UserId = Newcomer,
            Status = "pending", CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg("Acme", "27AABCU9603R1ZX")));

        // Re-running onboarding after asking to join must show "waiting for approval", not
        // re-offer a button whose only effect would be to return the same row.
        Assert.NotNull(ex.Conflict.ExistingRequest);
        Assert.Equal("pending", ex.Conflict.ExistingRequest!.Status);
    }

    [Fact]
    public async Task AnotherUsersPendingRequest_IsNotDisclosed()
    {
        using var db = await SeededAsync(nameof(AnotherUsersPendingRequest_IsNotDisclosed),
            gstin: "27AABCU9603R1ZX");
        db.OrgJoinRequests.Add(new OrgJoinRequest
        {
            Id = Guid.NewGuid(), OrgId = Existing, UserId = Guid.NewGuid(),   // somebody else
            Status = "pending", CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DuplicateOrganizationException>(
            () => Service(db).CreateAsync(Newcomer, NewOrg("Acme", "27AABCU9603R1ZX")));

        // The field is scoped to the caller. Echoing a colleague's pending request would leak
        // who else is trying to join a company the caller has not been admitted to.
        Assert.Null(ex.Conflict.ExistingRequest);
    }
}
