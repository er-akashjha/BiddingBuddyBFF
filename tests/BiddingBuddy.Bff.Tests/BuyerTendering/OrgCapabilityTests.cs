using BiddingBuddy.Bff.Core.Authorization;
using Xunit;

namespace BiddingBuddy.Bff.Tests.BuyerTendering;

/// <summary>
/// The capability map is the first authorization enforcement point in this API — until buyer-side
/// tendering there was none, and org membership alone decided everything.
///
/// <para>These tests pin the GePNIC separation of duties (docs/gov-tendering/PLAN.md §2.3): the
/// officer who drafts a tender is not, by default, the officer who publishes it. That separation is
/// the control that makes a department's file auditable, and an unenforced separation is worse than
/// no separation, because the audit file records it as though it held.</para>
/// </summary>
public sealed class OrgCapabilityTests
{
    // ── Separation of duties ────────────────────────────────────────────────

    [Fact]
    public void PoAdmin_authors_but_cannot_publish()
    {
        Assert.True(OrgCapabilities.Has(OrgRoles.PoAdmin, OrgCapabilities.TenderAuthor));
        Assert.False(OrgCapabilities.Has(OrgRoles.PoAdmin, OrgCapabilities.TenderPublish));
    }

    [Fact]
    public void PoPublisher_publishes_but_cannot_author()
    {
        Assert.True(OrgCapabilities.Has(OrgRoles.PoPublisher, OrgCapabilities.TenderPublish));
        Assert.False(OrgCapabilities.Has(OrgRoles.PoPublisher, OrgCapabilities.TenderAuthor));
    }

    [Fact]
    public void Auditor_reads_everything_and_changes_nothing()
    {
        // The whole shape of the role: full visibility including the audit file, zero write.
        Assert.True(OrgCapabilities.Has(OrgRoles.Auditor, OrgCapabilities.TenderRead));
        Assert.False(OrgCapabilities.Has(OrgRoles.Auditor, OrgCapabilities.TenderAuthor));
        Assert.False(OrgCapabilities.Has(OrgRoles.Auditor, OrgCapabilities.TenderPublish));
        Assert.False(OrgCapabilities.Has(OrgRoles.Auditor, OrgCapabilities.CommitteeManage));
    }

    [Theory]
    [InlineData(OrgRoles.PoOpener)]
    [InlineData(OrgRoles.PoEvaluator)]
    public void Phase3_roles_can_read_but_not_author_or_publish(string role)
    {
        // Opening and evaluation are Phase 3 duties — there is no bid to open until sealed bidding
        // exists. These roles are defined now so committee membership recorded on a Phase-1 tender
        // still means something later; in Phase 1 they read.
        Assert.True(OrgCapabilities.Has(role, OrgCapabilities.TenderRead));
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.TenderAuthor));
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.TenderPublish));
    }

    // ── Owner / admin ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrgRoles.Owner)]
    [InlineData(OrgRoles.Admin)]
    public void Owner_and_admin_hold_every_capability(string role)
    {
        // A workspace whose owner can be locked out of their own tenders is a support ticket, not a
        // security control. The separation that matters is surfaced as a warning at publish time
        // (SELF_PUBLISHED) rather than enforced into a dead end for a one-officer department.
        Assert.True(OrgCapabilities.Has(role, OrgCapabilities.TenderAuthor));
        Assert.True(OrgCapabilities.Has(role, OrgCapabilities.TenderPublish));
        Assert.True(OrgCapabilities.Has(role, OrgCapabilities.TenderRead));
        Assert.True(OrgCapabilities.Has(role, OrgCapabilities.CommitteeManage));
    }

    // ── Supplier roles hold nothing on the buyer surface ─────────────────────

    [Theory]
    [InlineData(OrgRoles.BidManager)]
    [InlineData(OrgRoles.Finance)]
    [InlineData(OrgRoles.Sales)]
    [InlineData(OrgRoles.Viewer)]
    public void Supplier_roles_hold_no_buyer_capability(string role)
    {
        // A bid manager at a supplier org has no business authoring a department's notice. A 'both'
        // type org assigns the buyer roles explicitly.
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.TenderAuthor));
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.TenderPublish));
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.TenderRead));
        Assert.False(OrgCapabilities.Has(role, OrgCapabilities.CommitteeManage));
    }

    // ── Fail closed ─────────────────────────────────────────────────────────

    [Fact]
    public void An_unknown_role_holds_nothing()
    {
        // Fails closed. A role added to the database CHECK constraint without a matching entry in
        // the capability map grants no access rather than defaulting to some baseline.
        Assert.False(OrgCapabilities.Has("procurement_wizard", OrgCapabilities.TenderRead));
    }

    [Fact]
    public void A_null_role_holds_nothing()
    {
        // GetUserRoleAsync returns null when there is no active membership.
        Assert.False(OrgCapabilities.Has(null, OrgCapabilities.TenderRead));
    }

    [Fact]
    public void Every_declared_role_appears_in_the_capability_map()
    {
        // The failure this catches: adding a role to OrgRoles and to the SQL CHECK, but forgetting
        // the map — which fails closed and therefore looks like a permissions bug rather than a
        // missing registration, and is very hard to recognise from the 403 alone.
        foreach (var role in OrgRoles.All)
            Assert.True(
                OrgCapabilities.Grants.ContainsKey(role),
                $"Role '{role}' is declared in OrgRoles.All but missing from OrgCapabilities.Grants.");
    }

    [Fact]
    public void Every_buyer_role_is_also_a_declared_role()
    {
        Assert.All(OrgRoles.BuyerRoles, r => Assert.Contains(r, OrgRoles.All));
    }
}
