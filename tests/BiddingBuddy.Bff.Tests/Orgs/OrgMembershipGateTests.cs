using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Orgs;

/// <summary>
/// There are two answers in this codebase to "which organizations can I use", and they have
/// to agree:
///
///   <see cref="OrganizationRepository.IsUserMemberAsync"/> — the gate
///   <c>OrgContextMiddleware</c> uses to decide 403 on every org-scoped request.
///   <see cref="OrganizationRepository.FindByUserIdAsync"/> — backs <c>GET /api/auth/me</c>,
///   which is what the SPA's org switcher lists and what it reconciles its cached org against.
///
/// They disagreed on deactivated organizations: the gate checked only the membership row, so
/// an org with <c>is_active = false</c> passed the middleware while being absent from
/// <c>/api/auth/me</c>. That gap is not academic now — the SPA re-validates its org against
/// <c>/api/auth/me</c> whenever a request 403s, so a source that says "you may use this org"
/// while the other says "this org does not exist" moves the user off a working org.
/// </summary>
public sealed class OrgMembershipGateTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<BffDbContext> SeededAsync(string name, Guid orgId, bool orgActive, string memberStatus)
    {
        var db = Db(name);
        db.Organizations.Add(new Organization { Id = orgId, Name = "Acme Supplies", OwnedBy = User, IsActive = orgActive });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgId, UserId = User, Role = "owner", Status = memberStatus });
        await db.SaveChangesAsync();
        return db;
    }

    [Theory]
    // org active, membership active → usable by both.
    [InlineData(true, "active", true)]
    // The case that was inconsistent: the membership row is live but the org is deactivated.
    // FindByUserIdAsync has always excluded this; the gate has to as well.
    [InlineData(false, "active", false)]
    // Suspended membership — both already agreed here.
    [InlineData(true, "suspended", false)]
    [InlineData(false, "suspended", false)]
    public async Task Gate_and_me_endpoint_agree_on_whether_an_org_is_usable(
        bool orgActive, string memberStatus, bool expectedUsable)
    {
        var orgId = Guid.NewGuid();
        await using var db = await SeededAsync(
            $"{nameof(Gate_and_me_endpoint_agree_on_whether_an_org_is_usable)}-{orgActive}-{memberStatus}",
            orgId, orgActive, memberStatus);
        var repo = new OrganizationRepository(db);

        var passesMiddlewareGate = await repo.IsUserMemberAsync(orgId, User);
        var listedByAuthMe = (await repo.FindByUserIdAsync(User)).Any(o => o.Id == orgId);

        Assert.Equal(expectedUsable, passesMiddlewareGate);
        Assert.Equal(expectedUsable, listedByAuthMe);
        // The point of the test: never one without the other.
        Assert.Equal(listedByAuthMe, passesMiddlewareGate);
    }

    /// <summary>
    /// The InMemory provider happily evaluates <c>m.Organization.IsActive</c> in memory even if
    /// the navigation is mapped to a shadow FK that does not exist in PostgreSQL — the test above
    /// would stay green while production threw <c>42703 undefined column</c>. So assert the real
    /// translation: the relational provider must join <c>organizations</c> on <c>org_members.org_id</c>
    /// (configured in <c>OrganizationConfiguration</c>), not on a conventional <c>OrganizationId</c>.
    ///
    /// No connection is opened — <c>ToQueryString()</c> only needs the provider's SQL generator.
    /// </summary>
    [Fact]
    public void Gate_translates_to_a_join_on_org_id_not_a_shadow_key()
    {
        using var db = new BffDbContext(new DbContextOptionsBuilder<BffDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=x;Password=y")
            .Options);

        var sql = db.OrgMembers
            .Where(m => m.OrgId == Guid.Empty
                     && m.UserId == User
                     && m.Status == "active"
                     && m.Organization.IsActive)
            .ToQueryString();

        Assert.Contains("organizations", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("org_id", sql, StringComparison.OrdinalIgnoreCase);
        // A shadow FK would surface as an OrganizationId column that has no counterpart in the DDL.
        Assert.DoesNotContain("OrganizationId", sql, StringComparison.OrdinalIgnoreCase);
    }
}
