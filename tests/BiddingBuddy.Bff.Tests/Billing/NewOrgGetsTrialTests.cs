using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using BiddingBuddy.Bff.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Billing;

/// <summary>
/// A workspace can be born in two places, and the trial has to be seeded in BOTH.
///
/// <para>This was a live bug found by exercising a running BFF: seeding lived only in
/// <c>OrganizationService.CreateAsync</c> (the onboarding form, which OAuth sign-ups reach),
/// while <c>AuthService.CreateVerifiedAccountAsync</c> creates the org inline the moment a
/// password sign-up confirms its e-mail OTP. Every password sign-up therefore landed on Free
/// with no trial — the "14-day trial · no credit card" promise, made in three places on the
/// marketing site, silently unkept for anyone who did not use a social provider.</para>
///
/// <para>The seeding itself is raw SQL the in-memory provider cannot run, so these tests pin
/// the wiring — that each path CALLS the seeder exactly once — while
/// <see cref="SubscriptionSeedPolicyTests"/> pins what the seeder decides.</para>
/// </summary>
public sealed class NewOrgGetsTrialTests
{
    private static BffDbContext Db() =>
        new(new DbContextOptionsBuilder<BffDbContext>()
            .UseInMemoryDatabase($"trialseed-{Guid.NewGuid()}").Options);

    private static OrganizationService Service(BffDbContext db, StubSubscriptionSeeder seeder) =>
        new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new StubPlanService(),
            seeder,
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    private static CreateOrgDto NewOrg(string? startPlan = null) =>
        new("Acme Supplies", null, null, null, null, null, null, null, null, null, null,
            null, null, null, false, startPlan);

    [Fact]
    public async Task Creating_a_workspace_seeds_a_subscription_exactly_once()
    {
        using var db = Db();
        var seeder = new StubSubscriptionSeeder();

        await Service(db, seeder).CreateAsync(Guid.NewGuid(), NewOrg());

        var call = Assert.Single(seeder.Calls);
        // No explicit choice → the seeder applies its default, the 14-day Growth trial.
        Assert.Null(call.StartPlan);
    }

    [Fact]
    public async Task An_explicit_free_choice_is_passed_through_to_the_seeder()
    {
        using var db = Db();
        var seeder = new StubSubscriptionSeeder();

        await Service(db, seeder).CreateAsync(Guid.NewGuid(), NewOrg(startPlan: PlanCatalog.Free));

        Assert.Equal(PlanCatalog.Free, Assert.Single(seeder.Calls).StartPlan);
    }

    [Fact]
    public async Task A_refused_duplicate_does_not_seed_anything()
    {
        // Nothing was created, so nothing should be entitled. A stray row here would give a
        // subscription to an org that does not exist.
        using var db = Db();
        db.Organizations.Add(new Organization
        {
            Id = Guid.NewGuid(), Name = "Acme Supplies", Gstin = "27AABCU9603R1ZX",
            OwnedBy = Guid.NewGuid(), IsActive = true,
        });
        await db.SaveChangesAsync();

        var seeder = new StubSubscriptionSeeder();
        var dto = new CreateOrgDto("Acme Supplies", null, "27AABCU9603R1ZX", null, null, null,
            null, null, null, null, null, null, null, null, false, null);

        await Assert.ThrowsAnyAsync<Exception>(() => Service(db, seeder).CreateAsync(Guid.NewGuid(), dto));

        Assert.Empty(seeder.Calls);
    }
}
