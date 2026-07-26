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

namespace BiddingBuddy.Bff.Tests.BuyerTendering;

/// <summary>
/// The inbound path to buyer status: an org asks, an operator decides, and approval runs the same
/// conversion an operator would run directly.
///
/// <para>The split is the design and these tests pin both halves of it: an owner/admin can RAISE a
/// request but raising it grants nothing, and only the operator approval flips <c>org_type</c> — via
/// <see cref="IOrganizationService.SetOrgTypeAsync"/>, so "requested then approved" and "provisioned
/// outright" stay one notion of a buyer rather than two.</para>
/// </summary>
public sealed class BuyerRequestServiceTests
{
    private static readonly Guid OwnerUser = Guid.NewGuid();
    private static readonly Guid AdminUser = Guid.NewGuid();
    private static readonly Guid ViewerUser = Guid.NewGuid();

    private static BffDbContext NewDb()
        => new(new DbContextOptionsBuilder<BffDbContext>()
            .UseInMemoryDatabase($"buyerreq-{Guid.NewGuid()}").Options);

    /// <summary>A real OrganizationService, so approval genuinely flips the org and writes the audit
    /// event rather than a stub pretending to.</summary>
    private static OrganizationService OrgService(BffDbContext db)
        => new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    private static BuyerRequestService Service(BffDbContext db, IConfiguration? config = null)
        => new(db,
            OrgService(db),
            Mock.Of<INotificationPublisher>(),
            new NotificationAudienceResolver(db),
            config ?? new ConfigurationBuilder().Build(),
            NullLogger<BuyerRequestService>.Instance);

    private static async Task<Guid> SeedOrgAsync(BffDbContext db, string orgType = "supplier")
    {
        var orgId = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = OwnerUser, Email = "owner@dept.gov.in", Name = "O Owner" },
            new User { Id = AdminUser, Email = "admin@dept.gov.in", Name = "A Admin" },
            new User { Id = ViewerUser, Email = "viewer@dept.gov.in", Name = "V Viewer" });
        db.Organizations.Add(new Organization { Id = orgId, OwnedBy = OwnerUser, Name = "PWD Kerala", OrgType = orgType });
        db.OrgMembers.AddRange(
            new OrgMember { Id = Guid.NewGuid(), OrgId = orgId, UserId = OwnerUser, Role = "owner", Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = orgId, UserId = AdminUser, Role = "admin", Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = orgId, UserId = ViewerUser, Role = "viewer", Status = "active" });
        await db.SaveChangesAsync();
        return orgId;
    }

    private static RequestBuyerAccessDto Req(string justification = "We are a state PWD and procure civil works.")
        => new(justification, EntityType: "state", Ministry: "Public Works", Department: "PWD");

    // ── Raising ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_admin_can_raise_a_request_and_it_starts_pending()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        var result = await Service(db).RequestAsync(orgId, AdminUser, Req());

        Assert.Equal("pending", result.Status);
        Assert.Equal("state", result.EntityType);
        Assert.Equal("A Admin", result.RequesterName);
    }

    [Fact]
    public async Task Raising_a_request_does_NOT_make_the_org_a_buyer()
    {
        // The whole point of the split: asking grants nothing.
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        await Service(db).RequestAsync(orgId, OwnerUser, Req());

        var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
        Assert.Equal("supplier", org.OrgType);
    }

    [Fact]
    public async Task A_viewer_cannot_raise_a_request()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).RequestAsync(orgId, ViewerUser, Req()));
    }

    [Fact]
    public async Task Justification_is_mandatory()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).RequestAsync(orgId, OwnerUser, Req(justification: "   ")));
    }

    [Fact]
    public async Task An_org_that_already_publishes_cannot_request()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db, orgType: "buyer");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).RequestAsync(orgId, OwnerUser, Req()));
        Assert.Equal("ALREADY_BUYER", ex.Message);
    }

    [Fact]
    public async Task Raising_twice_returns_the_same_pending_request()
    {
        // Idempotent: a double-tapped button must not stack rows for the operator to dismiss.
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);

        var first = await svc.RequestAsync(orgId, OwnerUser, Req());
        var second = await svc.RequestAsync(orgId, AdminUser, Req(justification: "different text"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.OrgBuyerRequests.CountAsync());
    }

    // ── Approval ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approval_flips_the_org_to_buyer_and_carries_the_claimed_identity()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var request = await svc.RequestAsync(orgId, OwnerUser, Req());

        var decided = await svc.ApproveAsync(request.Id, new ApproveBuyerRequestDto("Verified on a call"));

        Assert.Equal("approved", decided!.Status);
        var org = await db.Organizations.SingleAsync(o => o.Id == orgId);
        Assert.Equal("buyer", org.OrgType);
        // The identity claimed in the request was written onto the org, not dropped.
        Assert.Equal("state", org.EntityType);
        Assert.Equal("Public Works", org.Ministry);
    }

    [Fact]
    public async Task Approval_can_grant_both_for_a_PSU_that_also_bids()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var request = await svc.RequestAsync(orgId, OwnerUser, Req());

        await svc.ApproveAsync(request.Id, new ApproveBuyerRequestDto(OrgType: "both"));

        Assert.Equal("both", (await db.Organizations.SingleAsync(o => o.Id == orgId)).OrgType);
    }

    [Fact]
    public async Task Approval_writes_an_org_audit_event_via_the_shared_conversion()
    {
        // Proves approval goes through SetOrgTypeAsync rather than flipping the column itself —
        // which is what keeps the audit trail identical to a direct provisioning.
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var request = await svc.RequestAsync(orgId, OwnerUser, Req());

        await svc.ApproveAsync(request.Id, new ApproveBuyerRequestDto("phone verification"));

        var audit = await db.AuditEvents.SingleAsync(e => e.EntityType == "organization");
        Assert.Equal("org_type_changed", audit.Action);
        Assert.Contains(request.Id.ToString(), audit.Changes);   // the request id rode in on the VerificationNote
    }

    [Fact]
    public async Task A_request_already_decided_cannot_be_approved_again()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var request = await svc.RequestAsync(orgId, OwnerUser, Req());
        await svc.ApproveAsync(request.Id, new ApproveBuyerRequestDto());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ApproveAsync(request.Id, new ApproveBuyerRequestDto()));
        Assert.Equal("NOT_PENDING", ex.Message);
    }

    [Fact]
    public async Task Approving_an_unknown_request_returns_null()
    {
        using var db = NewDb();
        Assert.Null(await Service(db).ApproveAsync(Guid.NewGuid(), new ApproveBuyerRequestDto()));
    }

    // ── Rejection ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejection_requires_a_reason_and_leaves_the_org_a_supplier()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var request = await svc.RequestAsync(orgId, OwnerUser, Req());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RejectAsync(request.Id, new RejectBuyerRequestDto("  ")));

        var decided = await svc.RejectAsync(request.Id, new RejectBuyerRequestDto("Could not verify the department."));
        Assert.Equal("rejected", decided!.Status);
        Assert.Equal("Could not verify the department.", decided.DecisionNote);
        Assert.Equal("supplier", (await db.Organizations.SingleAsync(o => o.Id == orgId)).OrgType);
    }

    [Fact]
    public async Task A_rejected_org_can_raise_a_fresh_request()
    {
        // Decided rows are history; the partial unique index only bars a second PENDING row.
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var first = await svc.RequestAsync(orgId, OwnerUser, Req());
        await svc.RejectAsync(first.Id, new RejectBuyerRequestDto("Need registration proof."));

        var second = await svc.RequestAsync(orgId, OwnerUser, Req(justification: "Now attaching the Udyam certificate."));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("pending", second.Status);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task An_admin_can_withdraw_a_pending_request()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        await svc.RequestAsync(orgId, OwnerUser, Req());

        Assert.True(await svc.CancelAsync(orgId, AdminUser));
        Assert.Equal("cancelled", (await db.OrgBuyerRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_is_false_when_nothing_is_pending()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        Assert.False(await Service(db).CancelAsync(orgId, OwnerUser));
    }

    // ── Queue + current ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_operator_queue_lists_pending_requests_oldest_first_with_the_claim()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        await Service(db).RequestAsync(orgId, OwnerUser, Req());

        var queue = await Service(db).ListAsync(status: null);

        var row = Assert.Single(queue);
        Assert.Equal("PWD Kerala", row.OrgName);
        Assert.Equal("owner@dept.gov.in", row.RequesterEmail);   // raised by OwnerUser above
        Assert.Equal("state", row.EntityType);
    }

    [Fact]
    public async Task GetCurrent_returns_the_most_recent_request_of_any_status()
    {
        using var db = NewDb();
        var orgId = await SeedOrgAsync(db);
        var svc = Service(db);
        var first = await svc.RequestAsync(orgId, OwnerUser, Req());
        await svc.RejectAsync(first.Id, new RejectBuyerRequestDto("try again"));

        var current = await svc.GetCurrentAsync(orgId);

        Assert.Equal("rejected", current!.Status);
        Assert.Equal("try again", current.DecisionNote);
    }
}
