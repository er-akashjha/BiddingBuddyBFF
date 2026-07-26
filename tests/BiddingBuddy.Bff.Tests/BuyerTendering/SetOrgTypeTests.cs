using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Tests.BuyerTendering;

/// <summary>
/// Becoming a buyer organization is provisioned by an operator, never claimed by the org.
///
/// <para>The reason is not bureaucratic: a buyer org publishes tender notices that appear on the
/// public portal under a department's name. A self-serve checkbox would make "Directorate of Health
/// Services, Kerala" claimable by anyone with an email address, and this platform would carry the
/// result. So <c>CreateOrgDto</c> and <c>UpdateOrgDto</c> deliberately have no <c>orgType</c>, and
/// this is the only path — behind an API key rather than a user JWT.</para>
/// </summary>
public sealed class SetOrgTypeTests
{
    private static BffDbContext NewDb()
        => new(new DbContextOptionsBuilder<BffDbContext>()
            .UseInMemoryDatabase($"orgtype-{Guid.NewGuid()}")
            .Options);

    private static OrganizationService NewService(BffDbContext db)
        => new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    private static async Task<Guid> SeedOrgAsync(BffDbContext db)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            OwnedBy = Guid.NewGuid(),
            Name = "PWD Kerala",
            OrgType = "supplier",
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task Promotes_a_supplier_org_to_buyer_and_records_the_identity()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        var result = await NewService(db).SetOrgTypeAsync(orgId, new SetOrgTypeDto(
            OrgType: "buyer",
            EntityType: "state",
            Ministry: "Public Works",
            Department: "PWD",
            Office: "Trivandrum Circle",
            ProcuringEntityCode: "PWD-TVM-01",
            VerificationNote: "Verified against file F.No.12-3/2026-Admin"));

        Assert.NotNull(result);
        Assert.Equal("supplier", result.PreviousOrgType);
        Assert.Equal("buyer", result.OrgType);
        Assert.Equal("state", result.EntityType);
        Assert.Equal("PWD-TVM-01", result.ProcuringEntityCode);
    }

    [Fact]
    public async Task Writes_an_audit_event_naming_the_operator_and_the_change()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        await NewService(db).SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer", VerificationNote: "phone call with the Secretary"));

        var audit = await db.AuditEvents.SingleAsync(e => e.EntityId == orgId);

        Assert.Equal("organization", audit.EntityType);
        Assert.Equal("org_type_changed", audit.Action);
        // The caller is an API key, not a person. Attributing this to a user id would name whoever
        // happened to hold the key, which is worse than saying plainly that it was an operator.
        Assert.Null(audit.ActorId);
        Assert.Equal("operator", audit.ActorRole);
        Assert.Contains("supplier", audit.Changes);
        Assert.Contains("buyer", audit.Changes);
        Assert.Contains("phone call with the Secretary", audit.Changes);
    }

    [Fact]
    public async Task Re_granting_the_same_type_is_recorded_distinctly()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = NewService(db);

        await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer"));
        await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer"));

        var actions = await db.AuditEvents.Where(e => e.EntityId == orgId)
            .Select(e => e.Action).ToListAsync();

        Assert.Equal(["org_type_changed", "org_type_reaffirmed"], actions);
    }

    [Fact]
    public async Task Revoking_buyer_status_works_and_is_audited()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = NewService(db);

        await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer"));
        var result = await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("supplier"));

        Assert.Equal("buyer", result!.PreviousOrgType);
        Assert.Equal("supplier", result.OrgType);
        Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.EntityId == orgId));
    }

    [Theory]
    [InlineData("vendor")]
    [InlineData("Buyer")]      // wrong case — the CHECK is exact
    [InlineData("government")]
    [InlineData("")]
    public async Task An_invalid_org_type_is_rejected_with_a_usable_message(string orgType)
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        // Validated in the service rather than left to the database CHECK: a constraint violation
        // surfaces as a 500 with a constraint name in it, and an operator provisioning a department
        // deserves to be told which value was wrong and what the alternatives are.
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewService(db).SetOrgTypeAsync(orgId, new SetOrgTypeDto(orgType)));

        Assert.Contains("supplier", ex.Message);
        Assert.Contains("buyer", ex.Message);
        Assert.Contains("both", ex.Message);
    }

    [Fact]
    public async Task An_invalid_entity_type_is_rejected()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewService(db).SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer", EntityType: "galactic")));

        Assert.Contains("entityType", ex.Message);
    }

    [Fact]
    public async Task Nothing_is_written_when_validation_fails()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService(db).SetOrgTypeAsync(orgId, new SetOrgTypeDto("vendor")));

        var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
        Assert.Equal("supplier", org.OrgType);
        Assert.Empty(db.AuditEvents);
    }

    [Fact]
    public async Task An_unknown_org_returns_null_rather_than_throwing()
    {
        using var db = NewDb();

        Assert.Null(await NewService(db).SetOrgTypeAsync(Guid.NewGuid(), new SetOrgTypeDto("buyer")));
    }

    [Fact]
    public async Task Omitted_identity_fields_are_left_alone_rather_than_blanked()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = NewService(db);

        await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("buyer", Ministry: "Public Works", EntityType: "state"));
        // A later call that only corrects the type must not wipe the identity recorded earlier.
        var result = await svc.SetOrgTypeAsync(orgId, new SetOrgTypeDto("both"));

        Assert.Equal("Public Works", result!.Ministry);
        Assert.Equal("state", result.EntityType);
    }
}
