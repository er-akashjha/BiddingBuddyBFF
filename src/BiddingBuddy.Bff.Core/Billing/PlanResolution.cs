namespace BiddingBuddy.Bff.Core.Billing;

/// <summary>
/// Date-driven effective-plan resolution — the single source of truth for "what plan is
/// in force right now". Used by PlanService (gates) AND AuthService (/me projection) so
/// the two can never disagree.
///
/// Deliberately ignores the stored status label where dates can answer instead: the
/// lifecycle worker only flips labels and sends emails, so a stalled worker can never
/// extend paid entitlements past trial_ends_at / current_period_end + grace.
/// </summary>
public static class PlanResolution
{
    /// <summary>Paid access continues this long past current_period_end before dropping to free.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    public static (string PlanCode, string Status) Resolve(
        string? planCode, string? status, DateTime? trialEndsAt, DateTime? periodEnd, DateTime utcNow)
    {
        // No subscription row = free. Org creation seeds a trial row, but a failed seed
        // must degrade to free access, never to an error.
        if (planCode is null || status is null) return (PlanCatalog.Free, "active");

        switch (status)
        {
            case "trialing":
                return trialEndsAt is { } t && utcNow <= t
                    ? (planCode, "trialing")
                    : (PlanCatalog.Free, "expired");

            case "active":
            case "past_due":
                // No period end on an 'active' row is a deliberate ops hand-grant — honor it.
                if (periodEnd is not { } pe) return (planCode, "active");
                if (utcNow <= pe) return (planCode, "active");
                if (utcNow <= pe + Grace) return (planCode, "past_due");
                return (PlanCatalog.Free, "expired");

            default: // expired | canceled | anything unknown fails closed to free
                return (PlanCatalog.Free, status is "expired" or "canceled" ? status : "expired");
        }
    }
}
