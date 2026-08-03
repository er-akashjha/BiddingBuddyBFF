using BiddingBuddy.Bff.Core.Billing;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// The plan in force for an org right now: catalog values for the date-resolved plan code
/// with org_entitlement_overrides merged on top.
/// </summary>
public sealed record EffectivePlan(
    string PlanCode,
    string PlanName,
    string Status,            // trialing|active|past_due|expired|canceled (resolved label)
    DateTime? TrialEndsAt,
    DateTime? PeriodEnd,
    int SeatCap,
    int? AiSummariesPerMonth, // null = unlimited (fair use)
    int? SavedFilterCap,      // null = unlimited
    int AlertFloorMinutes,
    IReadOnlySet<string> Features)
{
    public bool HasFeature(string feature) => Features.Contains(feature);
}

public interface IPlanService
{
    /// <summary>Cached ~60s; call <see cref="Invalidate"/> after any subscription change.</summary>
    Task<EffectivePlan> GetPlanForAsync(Guid orgId, CancellationToken ct = default);

    void Invalidate(Guid orgId);
}
