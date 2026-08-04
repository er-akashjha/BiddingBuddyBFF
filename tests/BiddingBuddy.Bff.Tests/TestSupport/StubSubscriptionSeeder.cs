using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Tests.TestSupport;

/// <summary>
/// No-op seeder for service tests that are not about billing. The real one writes raw SQL,
/// which the in-memory provider cannot execute; the rule it applies is covered directly by
/// <c>SubscriptionSeedPolicyTests</c>. Records its calls so a test can assert that org
/// creation seeds exactly once.
/// </summary>
public sealed class StubSubscriptionSeeder : ISubscriptionSeeder
{
    public List<(Guid OrgId, string? StartPlan)> Calls { get; } = [];

    public Task SeedAsync(Guid orgId, string? startPlan, CancellationToken ct = default)
    {
        Calls.Add((orgId, startPlan));
        return Task.CompletedTask;
    }
}
