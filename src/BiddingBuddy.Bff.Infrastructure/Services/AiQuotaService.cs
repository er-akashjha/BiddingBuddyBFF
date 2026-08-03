using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// AI usage metering against ai_usage_records. The month bucket is the IST calendar month
/// ("resets on the 1st" — one shared mental model, no per-org anniversary math). Consume
/// is INSERT … ON CONFLICT DO NOTHING on the (org, feature, resource, month) unique
/// constraint, so re-viewing an unlocked resource never double-counts.
///
/// The count-then-insert pair is not one atomic step; two truly simultaneous first-time
/// unlocks can overshoot a cap by one. Accepted: the alternative is a serialized lock on
/// every AI view, to defend a quota whose smallest tier is 3/month.
/// </summary>
public class AiQuotaService(BffDbContext db, IPlanService planService) : IAiQuotaService
{
    // IANA id on Linux (prod containers), Windows id for local dev.
    private static readonly TimeZoneInfo Ist = ResolveIst();

    public async Task<AiQuotaVerdict> TryConsumeAsync(
        Guid orgId, Guid? userId, string feature, string resourceId, CancellationToken ct = default)
    {
        var plan = await planService.GetPlanForAsync(orgId, ct);
        var quota = plan.AiSummariesPerMonth;
        var month = CurrentPeriodMonth();

        var used = await db.AiUsageRecords.AsNoTracking()
            .CountAsync(r => r.OrgId == orgId && r.Feature == feature && r.PeriodMonth == month, ct);

        var unlocked = await db.AiUsageRecords.AsNoTracking()
            .AnyAsync(r => r.OrgId == orgId && r.Feature == feature
                        && r.ResourceId == resourceId && r.PeriodMonth == month, ct);
        if (unlocked)
            return new AiQuotaVerdict(Allowed: true, AlreadyUnlocked: true, used, quota);

        if (quota.HasValue && used >= quota.Value)
            return new AiQuotaVerdict(Allowed: false, AlreadyUnlocked: false, used, quota);

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ai_usage_records (org_id, user_id, feature, resource_id, period_month)
            VALUES ({orgId}, {userId}, {feature}, {resourceId}, {month})
            ON CONFLICT ON CONSTRAINT uq_ai_usage_org_feature_resource_month DO NOTHING", ct);

        // rowcount 0 = a concurrent request unlocked it first — still allowed, not re-counted.
        return new AiQuotaVerdict(Allowed: true, AlreadyUnlocked: inserted == 0, used + inserted, quota);
    }

    public async Task<(int Used, int? Quota)> GetUsageAsync(
        Guid orgId, string feature, CancellationToken ct = default)
    {
        var plan = await planService.GetPlanForAsync(orgId, ct);
        var month = CurrentPeriodMonth();
        var used = await db.AiUsageRecords.AsNoTracking()
            .CountAsync(r => r.OrgId == orgId && r.Feature == feature && r.PeriodMonth == month, ct);
        return (used, plan.AiSummariesPerMonth);
    }

    public Task<bool> IsUnlockedAsync(
        Guid orgId, string feature, string resourceId, CancellationToken ct = default)
    {
        var month = CurrentPeriodMonth();
        return db.AiUsageRecords.AsNoTracking()
            .AnyAsync(r => r.OrgId == orgId && r.Feature == feature
                        && r.ResourceId == resourceId && r.PeriodMonth == month, ct);
    }

    internal static DateOnly CurrentPeriodMonth()
    {
        var ist = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);
        return new DateOnly(ist.Year, ist.Month, 1);
    }

    private static TimeZoneInfo ResolveIst()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
