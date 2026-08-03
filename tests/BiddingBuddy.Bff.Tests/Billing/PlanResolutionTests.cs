using BiddingBuddy.Bff.Core.Billing;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Billing;

/// <summary>
/// Entitlement resolution is DATE-driven, never status-driven. That is the property that
/// makes a stalled or crashed lifecycle worker safe: it can leave a row labelled
/// "trialing" forever, and access still ends the moment the trial date passes.
/// </summary>
public sealed class PlanResolutionTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_subscription_row_resolves_to_free()
    {
        var (plan, status) = PlanResolution.Resolve(null, null, null, null, Now);
        Assert.Equal(PlanCatalog.Free, plan);
        Assert.Equal("active", status);
    }

    [Fact]
    public void Live_trial_grants_the_trial_plan()
    {
        var (plan, status) = PlanResolution.Resolve(
            PlanCatalog.Growth, "trialing", Now.AddDays(5), null, Now);
        Assert.Equal(PlanCatalog.Growth, plan);
        Assert.Equal("trialing", status);
    }

    /// <summary>The worker has not run yet — the row still says "trialing". Access must end anyway.</summary>
    [Fact]
    public void Expired_trial_drops_to_free_even_while_labelled_trialing()
    {
        var (plan, status) = PlanResolution.Resolve(
            PlanCatalog.Growth, "trialing", Now.AddSeconds(-1), null, Now);
        Assert.Equal(PlanCatalog.Free, plan);
        Assert.Equal("expired", status);
    }

    [Fact]
    public void Paid_period_grants_the_paid_plan()
    {
        var (plan, status) = PlanResolution.Resolve(
            PlanCatalog.Pro, "active", null, Now.AddDays(200), Now);
        Assert.Equal(PlanCatalog.Pro, plan);
        Assert.Equal("active", status);
    }

    /// <summary>A lapsed payment keeps working through the grace window rather than cutting off mid-bid.</summary>
    [Fact]
    public void Within_grace_after_period_end_keeps_the_plan_as_past_due()
    {
        var (plan, status) = PlanResolution.Resolve(
            PlanCatalog.Growth, "active", null, Now.AddDays(-3), Now);
        Assert.Equal(PlanCatalog.Growth, plan);
        Assert.Equal("past_due", status);
    }

    [Fact]
    public void Past_the_grace_window_drops_to_free()
    {
        var (plan, status) = PlanResolution.Resolve(
            PlanCatalog.Growth, "active", null, Now.AddDays(-8), Now);
        Assert.Equal(PlanCatalog.Free, plan);
        Assert.Equal("expired", status);
    }

    /// <summary>An ops hand-grant with no end date is honored rather than treated as lapsed.</summary>
    [Fact]
    public void Active_with_no_period_end_stays_active()
    {
        var (plan, status) = PlanResolution.Resolve(PlanCatalog.Pro, "active", null, null, Now);
        Assert.Equal(PlanCatalog.Pro, plan);
        Assert.Equal("active", status);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("expired")]
    [InlineData("something_we_never_wrote")]
    public void Terminal_and_unknown_statuses_fail_closed_to_free(string status)
    {
        var (plan, _) = PlanResolution.Resolve(PlanCatalog.Pro, status, null, Now.AddYears(1), Now);
        Assert.Equal(PlanCatalog.Free, plan);
    }

    [Fact]
    public void Catalog_prices_match_the_published_pricing_page()
    {
        // These are the numbers on the public pricing page and in the marketing copy.
        // A silent change here would sell a plan at a price we never advertised.
        Assert.Equal(0, PlanCatalog.Get(PlanCatalog.Free).PricePaiseAnnual);
        Assert.Equal(299_900, PlanCatalog.Get(PlanCatalog.Starter).PricePaiseAnnual);
        Assert.Equal(1_199_900, PlanCatalog.Get(PlanCatalog.Growth).PricePaiseAnnual);
        Assert.Equal(2_999_900, PlanCatalog.Get(PlanCatalog.Pro).PricePaiseAnnual);
    }

    [Fact]
    public void Competitors_is_a_growth_feature_and_free_holds_nothing()
    {
        Assert.Equal(PlanCatalog.Growth, PlanCatalog.LowestPlanWith(PlanFeatures.Competitors));
        Assert.Empty(PlanCatalog.Get(PlanCatalog.Free).Features);
        Assert.Equal(PlanCatalog.Pro, PlanCatalog.LowestPlanWith(PlanFeatures.Workflows));
    }

    [Fact]
    public void NextPlanUp_walks_the_ladder_and_stops_at_the_top()
    {
        Assert.Equal(PlanCatalog.Starter, PlanCatalog.NextPlanUp(PlanCatalog.Free));
        Assert.Equal(PlanCatalog.Growth, PlanCatalog.NextPlanUp(PlanCatalog.Starter));
        Assert.Equal(PlanCatalog.Pro, PlanCatalog.NextPlanUp(PlanCatalog.Growth));
        Assert.Null(PlanCatalog.NextPlanUp(PlanCatalog.Pro));
    }
}
