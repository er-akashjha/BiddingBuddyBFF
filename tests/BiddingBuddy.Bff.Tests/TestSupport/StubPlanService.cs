using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Tests.TestSupport;

/// <summary>
/// Permissive plan for service tests that are not about billing: caps high enough that
/// pre-billing tests keep their original behavior, every feature on. Tests that ARE about
/// plan limits construct their own <see cref="EffectivePlan"/> instead.
/// </summary>
public sealed class StubPlanService : IPlanService
{
    public EffectivePlan Plan { get; set; } = new(
        PlanCatalog.Pro, "Pro", "active",
        TrialEndsAt: null, PeriodEnd: null,
        SeatCap: int.MaxValue, AiSummariesPerMonth: null, SavedFilterCap: null,
        AlertFloorMinutes: 0,
        Features: new HashSet<string>
        {
            PlanFeatures.Competitors, PlanFeatures.EligibilityCheck, PlanFeatures.Workflows,
        });

    public Task<EffectivePlan> GetPlanForAsync(Guid orgId, CancellationToken ct = default)
        => Task.FromResult(Plan);

    public void Invalidate(Guid orgId) { }
}
