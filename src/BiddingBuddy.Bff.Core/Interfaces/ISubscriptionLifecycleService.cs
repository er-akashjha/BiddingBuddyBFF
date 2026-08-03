namespace BiddingBuddy.Bff.Core.Interfaces;

public sealed record SubscriptionLifecycleResult(
    int TrialEndingNotified, int TrialsExpired, int RenewalsNotified, int MarkedPastDue, int Expired);

/// <summary>
/// One pass over org_subscriptions: trial-ending nudges, renewal reminders, and status
/// transitions. Emails only — entitlements are already date-resolved by PlanService, so
/// this worker falling behind can never extend paid access.
/// </summary>
public interface ISubscriptionLifecycleService
{
    Task<SubscriptionLifecycleResult> RunAsync(CancellationToken ct = default);
}
