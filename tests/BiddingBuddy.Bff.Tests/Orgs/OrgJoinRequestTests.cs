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
/// The "let me in" half of duplicate handling. Being told your company already has a workspace
/// and given no way forward is what pushed people into creating the duplicate in the first
/// place, so the refusal has to end in an action.
///
/// <para>The invariant under test throughout: <b>a request never grants access</b>. Only an
/// owner or admin acting on it does, and they choose the role while acting — otherwise asking
/// to be an owner and being handed it is a one-line privilege escalation.</para>
/// </summary>
public sealed class OrgJoinRequestTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Viewer = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static JoinRequestService Service(BffDbContext db) =>
        new(db,
            Mock.Of<INotificationPublisher>(),
            new ConfigurationBuilder().Build(),
            NullLogger<JoinRequestService>.Instance);

    /// <summary>An active org with an owner and a viewer, plus an unaffiliated outsider.</summary>
    private static async Task<BffDbContext> SeededAsync(string dbName, bool orgActive = true)
    {
        var db = Db(dbName);

        db.Users.AddRange(
            new User { Id = Owner,    Name = "Priya Nair",  Email = "priya@acme.example" },
            new User { Id = Viewer,   Name = "Sam Viewer",  Email = "sam@acme.example" },
            new User { Id = Outsider, Name = "Rahul Desai", Email = "rahul@acme.example" });

        db.Organizations.Add(new Organization
        {
            Id = Org, Name = "Acme Supplies Pvt Ltd", OwnedBy = Owner, IsActive = orgActive,
        });
        db.OrgMembers.AddRange(
            new OrgMember { Id = Guid.NewGuid(), OrgId = Org, UserId = Owner,  Role = "owner",  Status = "active" },
            new OrgMember { Id = Guid.NewGuid(), OrgId = Org, UserId = Viewer, Role = "viewer", Status = "active" });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Guid> RequestAsync(BffDbContext db, Guid userId, string? message = null)
    {
        var result = await Service(db).RequestAsync(userId, new CreateJoinRequestDto(Org, message));
        return result.Id;
    }

    // ── Raising a request ─────────────────────────────────────────────────────

    [Fact]
    public async Task Request_CreatesPendingRow_ButNoMembership()
    {
        using var db = await SeededAsync(nameof(Request_CreatesPendingRow_ButNoMembership));

        var result = await Service(db).RequestAsync(Outsider, new CreateJoinRequestDto(Org, "Please add me"));

        Assert.Equal("pending", result.Status);
        Assert.Equal("Acme Supplies Pvt Ltd", result.OrgName);

        // The load-bearing assertion of this whole feature: asking is not joining.
        Assert.False(await db.OrgMembers.AnyAsync(m => m.OrgId == Org && m.UserId == Outsider));
    }

    [Fact]
    public async Task Request_IsIdempotent()
    {
        using var db = await SeededAsync(nameof(Request_IsIdempotent));

        var first  = await RequestAsync(db, Outsider);
        var second = await RequestAsync(db, Outsider);

        // A double-tapped button or a retry after a dropped response must not stack rows in the
        // approver's queue. The partial unique index would reject the second insert anyway —
        // returning the live row turns a 500 into the right answer.
        Assert.Equal(first, second);
        Assert.Equal(1, await db.OrgJoinRequests.CountAsync());
    }

    [Fact]
    public async Task Request_FromExistingMember_IsRejected()
    {
        using var db = await SeededAsync(nameof(Request_FromExistingMember_IsRejected));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).RequestAsync(Viewer, new CreateJoinRequestDto(Org, null)));

        Assert.Equal("ALREADY_MEMBER", ex.Message);
        Assert.Empty(await db.OrgJoinRequests.ToListAsync());
    }

    [Fact]
    public async Task Request_ForDeactivatedOrg_IsNotFound()
    {
        using var db = await SeededAsync(nameof(Request_ForDeactivatedOrg_IsNotFound), orgActive: false);

        // Queueing against a closed workspace would leave the request in a queue nobody opens.
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Service(db).RequestAsync(Outsider, new CreateJoinRequestDto(Org, null)));
    }

    // ── Deciding ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_CreatesMembershipWithTheApproversRole()
    {
        using var db = await SeededAsync(nameof(Approve_CreatesMembershipWithTheApproversRole));
        var requestId = await RequestAsync(db, Outsider);

        var member = await Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("bid_manager"));

        Assert.Equal("bid_manager", member.Role);
        Assert.Equal("active", member.Status);
        Assert.Equal("Rahul Desai", member.Name);

        var request = await db.OrgJoinRequests.SingleAsync();
        Assert.Equal("approved", request.Status);
        Assert.Equal(Owner, request.DecidedBy);
        Assert.Equal("bid_manager", request.Role);
    }

    [Fact]
    public async Task Approve_CannotGrantOwner()
    {
        using var db = await SeededAsync(nameof(Approve_CannotGrantOwner));
        var requestId = await RequestAsync(db, Outsider);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("owner")));

        // organizations.owned_by already names one owner, and RemoveMemberAsync refuses to
        // remove an owner — minting a second here would create a member nobody can remove.
        Assert.Equal("INVALID_ROLE", ex.Message);
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == Outsider));
    }

    [Fact]
    public async Task Approve_RejectsUnknownRole()
    {
        using var db = await SeededAsync(nameof(Approve_RejectsUnknownRole));
        var requestId = await RequestAsync(db, Outsider);

        // org_members.role carries a CHECK constraint. Letting an arbitrary string through would
        // turn a typo in the client into a 500 from Postgres instead of a 400 from us.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("superuser")));
    }

    [Fact]
    public async Task Approve_ReactivatesASuspendedMember()
    {
        using var db = await SeededAsync(nameof(Approve_ReactivatesASuspendedMember));
        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = Org, UserId = Outsider, Role = "viewer", Status = "suspended",
        });
        await db.SaveChangesAsync();
        var requestId = await RequestAsync(db, Outsider);

        await Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("sales"));

        // UNIQUE (org_id, user_id) would reject a second row, so a returning member has to be
        // reactivated in place — the same shape accepting an invite already uses.
        var member = Assert.Single(await db.OrgMembers.Where(m => m.UserId == Outsider).ToListAsync());
        Assert.Equal("active", member.Status);
        Assert.Equal("sales", member.Role);
    }

    [Fact]
    public async Task Reject_DecidesWithoutGrantingAnything()
    {
        using var db = await SeededAsync(nameof(Reject_DecidesWithoutGrantingAnything));
        var requestId = await RequestAsync(db, Outsider);

        await Service(db).RejectAsync(Org, requestId, Owner);

        Assert.Equal("rejected", (await db.OrgJoinRequests.SingleAsync()).Status);
        Assert.False(await db.OrgMembers.AnyAsync(m => m.UserId == Outsider));
    }

    [Fact]
    public async Task DecidingTwice_IsRefused()
    {
        using var db = await SeededAsync(nameof(DecidingTwice_IsRefused));
        var requestId = await RequestAsync(db, Outsider);
        await Service(db).RejectAsync(Org, requestId, Owner);

        // Two admins with the queue open. The first decision stands rather than the second
        // silently overwriting it.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("viewer")));

        Assert.Equal("REQUEST_ALREADY_DECIDED", ex.Message);
    }

    // ── Who may decide ────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_CannotApprove()
    {
        using var db = await SeededAsync(nameof(Viewer_CannotApprove));
        var requestId = await RequestAsync(db, Outsider);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).ApproveAsync(Org, requestId, Viewer, new ApproveJoinRequestDto("admin")));
    }

    [Fact]
    public async Task Viewer_CannotEvenSeeTheQueue()
    {
        using var db = await SeededAsync(nameof(Viewer_CannotEvenSeeTheQueue));
        await RequestAsync(db, Outsider, "Please add me");

        // The queue carries the name, email and free-text note of everyone applying. That is
        // admin-only for the same reason the decision is.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).GetPendingForOrgAsync(Org, Viewer));
    }

    [Fact]
    public async Task Outsider_CannotDecideForAnOrgTheyAreNotIn()
    {
        using var db = await SeededAsync(nameof(Outsider_CannotDecideForAnOrgTheyAreNotIn));
        var requestId = await RequestAsync(db, Outsider);

        // Approving one's own request would be the entire access control model defeated.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Service(db).ApproveAsync(Org, requestId, Outsider, new ApproveJoinRequestDto("admin")));
    }

    // ── Requester-side views ──────────────────────────────────────────────────

    [Fact]
    public async Task Queue_ShowsRequesterDetailsOldestFirst()
    {
        using var db = await SeededAsync(nameof(Queue_ShowsRequesterDetailsOldestFirst));
        await RequestAsync(db, Outsider, "New bid manager here");

        var queue = await Service(db).GetPendingForOrgAsync(Org, Owner);

        var row = Assert.Single(queue);
        Assert.Equal("Rahul Desai", row.UserName);
        Assert.Equal("rahul@acme.example", row.UserEmail);
        Assert.Equal("New bid manager here", row.Message);
    }

    [Fact]
    public async Task Cancel_WithdrawsOwnRequest()
    {
        using var db = await SeededAsync(nameof(Cancel_WithdrawsOwnRequest));
        var requestId = await RequestAsync(db, Outsider);

        await Service(db).CancelAsync(Outsider, requestId);

        Assert.Equal("cancelled", (await db.OrgJoinRequests.SingleAsync()).Status);
        Assert.Empty(await Service(db).GetPendingForOrgAsync(Org, Owner));
    }

    [Fact]
    public async Task Cancel_CannotTouchSomeoneElsesRequest()
    {
        using var db = await SeededAsync(nameof(Cancel_CannotTouchSomeoneElsesRequest));
        var requestId = await RequestAsync(db, Outsider);

        // Scoped by user id, so a stray id from another session cannot withdraw an application
        // the caller did not make.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db).CancelAsync(Viewer, requestId));
        Assert.Equal("pending", (await db.OrgJoinRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_AfterDecision_IsANoOp()
    {
        using var db = await SeededAsync(nameof(Cancel_AfterDecision_IsANoOp));
        var requestId = await RequestAsync(db, Outsider);
        await Service(db).ApproveAsync(Org, requestId, Owner, new ApproveJoinRequestDto("viewer"));

        await Service(db).CancelAsync(Outsider, requestId);

        // Silent rather than an error: the user's intent ("stop this being pending") is already
        // satisfied, and flipping an approved row to cancelled would strand a real membership.
        Assert.Equal("approved", (await db.OrgJoinRequests.SingleAsync()).Status);
        Assert.True(await db.OrgMembers.AnyAsync(m => m.UserId == Outsider && m.Status == "active"));
    }

    [Fact]
    public async Task Mine_ShowsPendingAndRecentDecisions()
    {
        using var db = await SeededAsync(nameof(Mine_ShowsPendingAndRecentDecisions));
        var requestId = await RequestAsync(db, Outsider);
        await Service(db).RejectAsync(Org, requestId, Owner);

        var mine = await Service(db).GetMineAsync(Outsider);

        // A rejection the requester never sees looks identical to one that was never answered.
        var row = Assert.Single(mine);
        Assert.Equal("rejected", row.Status);
        Assert.Equal("Acme Supplies Pvt Ltd", row.OrgName);
    }

    [Fact]
    public async Task Mine_HidesDecisionsOlderThanThirtyDays()
    {
        using var db = await SeededAsync(nameof(Mine_HidesDecisionsOlderThanThirtyDays));
        db.OrgJoinRequests.Add(new OrgJoinRequest
        {
            Id = Guid.NewGuid(), OrgId = Org, UserId = Outsider, Status = "rejected",
            CreatedAt = DateTime.UtcNow.AddDays(-90), DecidedAt = DateTime.UtcNow.AddDays(-89),
        });
        await db.SaveChangesAsync();

        // Otherwise the onboarding page is haunted forever by a rejection from months ago.
        Assert.Empty(await Service(db).GetMineAsync(Outsider));
    }
}
