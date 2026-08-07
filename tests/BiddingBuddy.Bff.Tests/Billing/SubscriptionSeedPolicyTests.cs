using BiddingBuddy.Bff.Core.Billing;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Billing;

/// <summary>
/// The opening subscription for a new workspace. The important property is not that Free
/// works — it is that NOTHING ELSE does: <c>startPlan</c> arrives from a public sign-up
/// form, so a value that isn't "free" must never become a paid entitlement.
/// </summary>
public sealed class SubscriptionSeedPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 4, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void No_request_starts_the_growth_trial()
    {
        var seed = SubscriptionSeedPolicy.For(null, Now);

        Assert.Equal(PlanCatalog.Growth, seed.PlanCode);
        Assert.Equal("trialing", seed.Status);
        Assert.Equal(Now.AddDays(14), seed.TrialEndsAt);
    }

    [Fact]
    public void Choosing_free_skips_the_trial_entirely()
    {
        var seed = SubscriptionSeedPolicy.For("free", Now);

        Assert.Equal(PlanCatalog.Free, seed.PlanCode);
        Assert.Equal("active", seed.Status);
        // No trial end: someone who declined the trial must not be handed a clock that
        // later "expires" them out of a plan they deliberately chose.
        Assert.Null(seed.TrialEndsAt);
    }

    [Theory]
    [InlineData("FREE")]
    [InlineData("Free")]
    public void Free_is_matched_case_insensitively(string requested)
        => Assert.Equal(PlanCatalog.Free, SubscriptionSeedPolicy.For(requested, Now).PlanCode);

    /// <summary>
    /// The security-relevant case: posting a paid plan code must not grant it. These fall
    /// back to the trial, which expires on its own, rather than to a paid entitlement
    /// nobody paid for.
    /// </summary>
    [Theory]
    [InlineData("pro")]
    [InlineData("growth")]
    [InlineData("starter")]
    [InlineData("enterprise")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("free ")]          // trailing space — not an exact match, so not honoured
    [InlineData("'; DROP TABLE org_subscriptions; --")]
    public void Anything_other_than_free_falls_back_to_the_trial(string requested)
    {
        var seed = SubscriptionSeedPolicy.For(requested, Now);

        Assert.Equal(PlanCatalog.Growth, seed.PlanCode);
        Assert.Equal("trialing", seed.Status);
        Assert.NotNull(seed.TrialEndsAt);
    }

    [Fact]
    public void Trial_length_matches_what_the_marketing_site_promises()
        => Assert.Equal(14, SubscriptionSeedPolicy.TrialDays);
}
