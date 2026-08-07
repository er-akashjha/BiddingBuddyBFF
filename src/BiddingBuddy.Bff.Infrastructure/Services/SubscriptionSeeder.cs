using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <inheritdoc />
public class SubscriptionSeeder(BffDbContext db, ILogger<SubscriptionSeeder> log) : ISubscriptionSeeder
{
    public async Task SeedAsync(Guid orgId, string? startPlan, CancellationToken ct = default)
    {
        // SubscriptionSeedPolicy owns the rule (and its tests): only "free" is honoured,
        // anything else falls back to the trial, so this can never be used to
        // self-assign a paid plan.
        var seed = SubscriptionSeedPolicy.For(startPlan, DateTime.UtcNow);

        try
        {
            // ON CONFLICT DO NOTHING keeps this safe to call from more than one path for
            // the same org — the first writer wins and a second call is a no-op.
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO org_subscriptions (org_id, plan_code, status, trial_ends_at)
                VALUES ({orgId}, {seed.PlanCode}, {seed.Status}, {seed.TrialEndsAt})
                ON CONFLICT (org_id) DO NOTHING", ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "Could not seed the subscription for org {OrgId}; it falls back to the free plan.", orgId);
        }
    }
}
