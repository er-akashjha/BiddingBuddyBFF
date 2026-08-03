using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Resolves the effective plan for an org: date-driven resolution (PlanResolution) over
/// the org_subscriptions row, catalog lookup, then org_entitlement_overrides merged on
/// top. Cached 60s per org — short enough that a payment feels instant even without the
/// explicit <see cref="Invalidate"/> the activation path performs.
/// </summary>
public class PlanService(BffDbContext db, IMemoryCache cache) : IPlanService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private static string Key(Guid orgId) => $"plan:{orgId}";

    public async Task<EffectivePlan> GetPlanForAsync(Guid orgId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Key(orgId), out EffectivePlan? cached) && cached is not null)
            return cached;

        var now = DateTime.UtcNow;

        var sub = await db.OrgSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrgId == orgId, ct);

        var overrides = await db.OrgEntitlementOverrides.AsNoTracking()
            .Where(o => o.OrgId == orgId && (o.ExpiresAt == null || o.ExpiresAt > now))
            .ToListAsync(ct);

        var (code, status) = PlanResolution.Resolve(
            sub?.PlanCode, sub?.Status, sub?.TrialEndsAt, sub?.CurrentPeriodEnd, now);

        var def = PlanCatalog.Get(code);

        var seatCap = def.SeatCap;
        var aiQuota = def.AiSummariesPerMonth;
        var filterCap = def.SavedFilterCap;
        var features = new HashSet<string>(def.Features, StringComparer.Ordinal);

        foreach (var o in overrides)
        {
            if (o.Key == "seat_cap" && int.TryParse(o.Value, out var seats))
                seatCap = seats;
            else if (o.Key == "ai_quota")
                aiQuota = o.Value.Equals("unlimited", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : int.TryParse(o.Value, out var q) ? q : aiQuota;
            else if (o.Key == "saved_filter_cap" && int.TryParse(o.Value, out var f))
                filterCap = f;
            else if (o.Key.StartsWith("feature:", StringComparison.Ordinal))
            {
                var feature = o.Key["feature:".Length..];
                if (bool.TryParse(o.Value, out var on) && !on) features.Remove(feature);
                else features.Add(feature);
            }
        }

        var plan = new EffectivePlan(
            def.Code, def.Name, status,
            sub?.TrialEndsAt, sub?.CurrentPeriodEnd,
            seatCap, aiQuota, filterCap, def.AlertFloorMinutes, features);

        cache.Set(Key(orgId), plan, Ttl);
        return plan;
    }

    public void Invalidate(Guid orgId) => cache.Remove(Key(orgId));
}
