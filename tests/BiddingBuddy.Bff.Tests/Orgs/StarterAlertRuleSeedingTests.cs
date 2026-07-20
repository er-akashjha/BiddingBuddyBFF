using BiddingBuddy.Bff.Core.DTOs.Alerts;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
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
/// Onboarding promises "we'll surface the most relevant government tenders for you right away", but
/// <c>organizations.primary_category</c> was write-only — stored, echoed in DTOs, and read by nothing.
/// The sector now seeds a real <c>tender_alert_rules</c> row, which is what MatchingService actually
/// matches on.
///
/// Two properties are worth pinning because both fail silently. The rule must be seeded with the
/// category string VERBATIM — category matching is full-string OrdinalIgnoreCase, so a mangled label
/// matches zero tenders forever rather than erroring. And seeding must be idempotent — the table has
/// no unique constraint, so a duplicate is accepted by the database and only shows up as a second row
/// the user has to find and delete in Settings → Interests.
/// </summary>
public sealed class StarterAlertRuleSeedingTests
{
    private const string Sector = "Computers & IT Hardware";

    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static OrganizationService Service(BffDbContext db) =>
        new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    /// <summary>Only the sector varies here; every other key stays null = untouched.</summary>
    private static UpdateOrgDto Sectored(string? primaryCategory) =>
        new(null, null, null, null, null, null, null, null, null, null, null,
            null, null, primaryCategory, null);

    private static CreateOrgDto NewOrg(string? primaryCategory) =>
        new("Acme Supplies", null, null, null, null, null, null, null, null, null, null,
            null, null, primaryCategory);

    /// <summary>An org that already exists without a sector — onboarding's PATCH mode.</summary>
    private static async Task<BffDbContext> SeededAsync(string name, string? primaryCategory = null)
    {
        var db = Db(name);
        db.Organizations.Add(new Organization
        {
            Id = Org, Name = "Acme Supplies", OwnedBy = Owner, IsActive = true,
            PrimaryCategory = primaryCategory,
        });
        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = Org, UserId = Owner, Role = "owner", Status = "active",
        });
        await db.SaveChangesAsync();
        return db;
    }

    // ── The promise ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatingOrgWithSector_SeedsMatchingRule()
    {
        using var db = Db(nameof(CreatingOrgWithSector_SeedsMatchingRule));

        await Service(db).CreateAsync(Owner, NewOrg(Sector), default);

        var rule = await db.TenderAlertRules.SingleAsync();
        // Verbatim, single-element: MatchingService compares the whole string OrdinalIgnoreCase
        // against tenders.category, so "Computers" or "computers & it hardware " matches nothing.
        Assert.Equal([Sector], rule.Categories);
        Assert.True(rule.IsActive);
        Assert.Equal(Owner, rule.CreatedBy);
    }

    [Fact]
    public async Task SettingSectorOnExistingOrg_SeedsMatchingRule()
    {
        using var db = await SeededAsync(nameof(SettingSectorOnExistingOrg_SeedsMatchingRule));

        // AuthService creates the org with a name only, so onboarding mode 1 PATCHes the sector in
        // afterwards. That path has to seed too, or social signups never get a rule at all.
        await Service(db).UpdateAsync(Org, Owner, Sectored(Sector), default);

        var rule = await db.TenderAlertRules.SingleAsync();
        Assert.Equal([Sector], rule.Categories);
        Assert.Equal(Org, rule.OrgId);
    }

    [Fact]
    public async Task SeededRule_ConstrainsCategoryOnly()
    {
        using var db = Db(nameof(SeededRule_ConstrainsCategoryOnly));

        await Service(db).CreateAsync(Owner, NewOrg(Sector), default);

        var rule = await db.TenderAlertRules.SingleAsync();
        // Constraints are ANDed, and a value-bounded rule drops every tender whose value is null.
        // The starter rule knows only the sector, so anything else it invented would narrow the
        // feed on a guess the user never made.
        Assert.Null(rule.States);
        Assert.Null(rule.Keywords);
        Assert.Null(rule.MinValue);
        Assert.Null(rule.MaxValue);
        Assert.Null(rule.MinAiScore);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RerunningOnboarding_DoesNotDuplicateRule()
    {
        using var db = await SeededAsync(nameof(RerunningOnboarding_DoesNotDuplicateRule));
        var svc = Service(db);

        await svc.UpdateAsync(Org, Owner, Sectored(Sector), default);
        await svc.UpdateAsync(Org, Owner, Sectored(Sector), default);

        Assert.Equal(1, await db.TenderAlertRules.CountAsync());
    }

    [Fact]
    public async Task ChangingSectorLater_DoesNotSeedAgain()
    {
        using var db = await SeededAsync(nameof(ChangingSectorLater_DoesNotSeedAgain), primaryCategory: Sector);

        await Service(db).UpdateAsync(Org, Owner, Sectored("Medical Equipment & Supplies"), default);

        // Seeding is a first-set bootstrap, not a sync. Once the sector is set the org's rules are
        // the user's to curate — a second rule here would quietly widen their feed.
        Assert.Empty(await db.TenderAlertRules.ToListAsync());
    }

    [Fact]
    public async Task OrgWithExistingInterests_IsLeftAlone()
    {
        using var db = await SeededAsync(nameof(OrgWithExistingInterests_IsLeftAlone));
        await new TenderAlertRuleService(db).CreateAsync(Org, Owner,
            new CreateTenderAlertRuleDto("Hand-built", ["Textiles & Apparel"], null, ["uniform"],
                null, null, null), default);

        await Service(db).UpdateAsync(Org, Owner, Sectored(Sector), default);

        // A user who already curated interests has said what they want more precisely than the
        // sector picker can. Adding a broad category rule alongside would OR itself in and flood them.
        var rule = Assert.Single(await db.TenderAlertRules.ToListAsync());
        Assert.Equal("Hand-built", rule.Name);
    }

    // ── No sector, no rule ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatingOrgWithoutSector_SeedsNothing()
    {
        using var db = Db(nameof(CreatingOrgWithoutSector_SeedsNothing));

        // The onboarding page offers "Create without sector" explicitly.
        await Service(db).CreateAsync(Owner, NewOrg(null), default);

        Assert.Empty(await db.TenderAlertRules.ToListAsync());
    }

    [Fact]
    public async Task BlankSector_SeedsNothing()
    {
        using var db = await SeededAsync(nameof(BlankSector_SeedsNothing));

        await Service(db).UpdateAsync(Org, Owner, Sectored("   "), default);

        // A blank category on a rule would be normalised away to null, leaving an unconstrained
        // rule that matches EVERY live tender — the loudest possible failure mode.
        Assert.Empty(await db.TenderAlertRules.ToListAsync());
    }

    [Fact]
    public async Task PatchingOtherFields_SeedsNothing()
    {
        using var db = await SeededAsync(nameof(PatchingOtherFields_SeedsNothing));

        await Service(db).UpdateAsync(Org, Owner,
            new UpdateOrgDto("Renamed Ltd", null, null, null, null, null, null, null, null, null,
                null, null, null, null, null), default);

        Assert.Empty(await db.TenderAlertRules.ToListAsync());
    }
}
