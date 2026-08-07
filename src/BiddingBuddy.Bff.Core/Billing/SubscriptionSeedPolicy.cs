namespace BiddingBuddy.Bff.Core.Billing;

/// <summary>What a brand-new workspace's <c>org_subscriptions</c> row should say.</summary>
public sealed record SubscriptionSeed(string PlanCode, string Status, DateTime? TrialEndsAt);

/// <summary>
/// Decides the opening subscription for a new workspace from the plan the signer-up asked
/// for on /pricing.
///
/// <para>Pulled out of <c>OrganizationService</c> as a pure function for one reason: the
/// seeding itself is raw SQL, which the in-memory provider used by the service tests cannot
/// execute, so the RULE would otherwise ship with no coverage at all. The rule is worth
/// covering — "anything that isn't 'free' becomes the trial" is what stops a client from
/// self-assigning Pro by posting <c>startPlan: "pro"</c>.</para>
/// </summary>
public static class SubscriptionSeedPolicy
{
    public const int TrialDays = 14;

    public static SubscriptionSeed For(string? requestedPlan, DateTime utcNow)
    {
        // Only Free is honoured. Every other value — a paid code, a typo, null — falls
        // through to the trial. Never throws: a bad value must not fail signup.
        if (string.Equals(requestedPlan, PlanCatalog.Free, StringComparison.OrdinalIgnoreCase))
            return new SubscriptionSeed(PlanCatalog.Free, "active", null);

        return new SubscriptionSeed(PlanCatalog.Growth, "trialing", utcNow.AddDays(TrialDays));
    }
}
