using BiddingBuddy.Bff.Core.DTOs.Analysis;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class AnalysisService(BffDbContext db) : IAnalysisService
{
    public async Task<AiAnalysisResultDto?> GetTenderAnalysisAsync(Guid tenderId, Guid orgId, CancellationToken ct = default)
    {
        // Verify org has access (tender is tracked or saved by org)
        var hasAccess = await db.Tenders.AnyAsync(t => t.Id == tenderId, ct);
        if (!hasAccess) throw new KeyNotFoundException("Tender not found.");

        var analysis = await db.AiAnalysisResults
            .FirstOrDefaultAsync(a => a.TenderId == tenderId, ct);

        if (analysis is null) return null;

        return new AiAnalysisResultDto(
            analysis.Id, analysis.ModelUsed,
            analysis.EligibilityBreakdown, analysis.RiskFactors,
            analysis.WinStrategy, analysis.SuggestedBidRange,
            analysis.RequiredDocuments, analysis.KeyClauses,
            analysis.GeneratedAt);
    }

    public async Task<IReadOnlyList<PerformanceSnapshotDto>> GetPerformanceSnapshotsAsync(
        Guid orgId, int limit, CancellationToken ct = default)
    {
        return await db.OrgPerformanceSnapshots
            .Where(s => s.OrgId == orgId)
            .OrderByDescending(s => s.SnapshotDate)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(s => new PerformanceSnapshotDto(
                s.Id,
                s.SnapshotDate.ToString("yyyy-MM"),
                s.TotalBids ?? 0,
                s.BidsWon ?? 0,
                s.BidsLost ?? 0,
                (s.TotalBids ?? 0) - (s.BidsWon ?? 0) - (s.BidsLost ?? 0),
                s.WinRate,
                s.WonValue,
                s.AvgBidValue,
                null,  // TopCategory from JSON — left for future
                null,  // TopState from JSON — left for future
                s.CreatedAt))
            .ToListAsync(ct);
    }

    // ── Reports & Analytics page (live queries; org_performance_snapshots unused) ──

    public async Task<KpisDto> GetKpisAsync(Guid orgId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        (from, to) = NormalizePeriod(from, to);

        // Tenders scraped in the period — global count (tenders are not org-scoped).
        var tendersAnalyzed = await db.Tenders
            .Where(t => t.CreatedAt >= from && t.CreatedAt < to)
            .CountAsync(ct);

        // Org's bids in the period, with stage at-or-past 'submitted'.
        var bidsInPeriod = db.Bids.Where(b => b.OrgId == orgId
                                           && b.CreatedAt >= from && b.CreatedAt < to);

        var bidsSubmitted = await bidsInPeriod
            .Where(b => b.Stage == "submitted" || b.Stage == "won" || b.Stage == "lost")
            .CountAsync(ct);

        var wonCount  = await bidsInPeriod.CountAsync(b => b.Stage == "won",  ct);
        var lostCount = await bidsInPeriod.CountAsync(b => b.Stage == "lost", ct);
        var winRate   = (wonCount + lostCount) == 0
            ? 0m
            : Math.Round((decimal)wonCount * 100m / (wonCount + lostCount), 2);

        var revenueWon = await bidsInPeriod
            .Where(b => b.Stage == "won")
            .SumAsync(b => (decimal?)b.WonValue, ct) ?? 0m;

        // Open pipeline — value of every org bid not yet won/lost. This is current
        // state, NOT period-scoped: a still-active bid created before `from` is part
        // of the live pipeline. Per bid, prefer our quoted value, else the tender value.
        var pipelineValue = await db.Bids
            .Where(b => b.OrgId == orgId && b.Stage != "won" && b.Stage != "lost")
            .SumAsync(b => b.OurBidValue ?? b.TenderValue ?? 0m, ct);

        return new KpisDto(tendersAnalyzed, bidsSubmitted, winRate, revenueWon, pipelineValue, from, to);
    }

    public async Task<DashboardDto> GetDashboardAsync(Guid orgId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        (from, to) = NormalizePeriod(from, to);

        // Generate the list of month buckets up-front so empty months stay in the
        // result with zeros — matters for chart x-axis continuity.
        var months = MonthsBetween(from, to);

        // ── Tender Activity: discovered (global) + won (org-scoped), per month ──
        var discoveredByMonth = (await db.Tenders
                .Where(t => t.CreatedAt >= from && t.CreatedAt < to)
                .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => Key(x.Year, x.Month), x => x.Count);

        var wonBidsByMonth = (await db.Bids
                .Where(b => b.OrgId == orgId
                         && b.Stage == "won"
                         && b.UpdatedAt >= from && b.UpdatedAt < to)
                .GroupBy(b => new { b.UpdatedAt.Year, b.UpdatedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => Key(x.Year, x.Month), x => x.Count);

        var tenderActivity = months
            .Select(m => new TenderActivityPointDto(
                m.Key,
                discoveredByMonth.GetValueOrDefault(m.Key, 0),
                wonBidsByMonth.GetValueOrDefault(m.Key, 0)))
            .ToList();

        // ── Win/Loss by Category (joins bids → tenders, terminal stages only) ──
        // Note: aliased to fromUtc/toUtc inside the LINQ query because `from` is
        // a reserved keyword in C# query syntax.
        var fromUtc = from;
        var toUtc   = to;
        var winLossRaw = await (
            from b in db.Bids
            join t in db.Tenders on b.TenderId equals t.Id
            where b.OrgId == orgId
               && b.UpdatedAt >= fromUtc && b.UpdatedAt < toUtc
               && (b.Stage == "won" || b.Stage == "lost")
               && t.Category != null && t.Category != ""
            group b by new { t.Category, b.Stage } into g
            select new { Category = g.Key.Category!, g.Key.Stage, Count = g.Count() }
        ).ToListAsync(ct);

        var winLossByCategory = winLossRaw
            .GroupBy(x => x.Category)
            .Select(g => new WinLossByCategoryDto(
                g.Key,
                Won:  g.Where(x => x.Stage == "won").Sum(x => x.Count),
                Lost: g.Where(x => x.Stage == "lost").Sum(x => x.Count)))
            .OrderByDescending(x => x.Won + x.Lost)
            .ToList();

        // ── Revenue by Month (sum of won_value bucketed by month wins were recorded) ──
        var revenueByMonthRaw = (await db.Bids
                .Where(b => b.OrgId == orgId
                         && b.Stage == "won"
                         && b.WonValue != null
                         && b.UpdatedAt >= from && b.UpdatedAt < to)
                .GroupBy(b => new { b.UpdatedAt.Year, b.UpdatedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Sum = g.Sum(b => b.WonValue) ?? 0m })
                .ToListAsync(ct))
            .ToDictionary(x => Key(x.Year, x.Month), x => x.Sum);

        var revenueByMonth = months
            .Select(m => new RevenueByMonthDto(m.Key, revenueByMonthRaw.GetValueOrDefault(m.Key, 0m)))
            .ToList();

        // ── Win Rate Over Time (per-month win rate for terminal bids) ──
        var terminalsByMonth = (await db.Bids
                .Where(b => b.OrgId == orgId
                         && (b.Stage == "won" || b.Stage == "lost")
                         && b.UpdatedAt >= from && b.UpdatedAt < to)
                .GroupBy(b => new { b.UpdatedAt.Year, b.UpdatedAt.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Won  = g.Count(b => b.Stage == "won"),
                    Lost = g.Count(b => b.Stage == "lost"),
                })
                .ToListAsync(ct))
            .ToDictionary(x => Key(x.Year, x.Month), x => x);

        var winRateOverTime = months
            .Select(m =>
            {
                if (!terminalsByMonth.TryGetValue(m.Key, out var t) || (t.Won + t.Lost) == 0)
                    return new WinRateOverTimePointDto(m.Key, 0m);
                var rate = Math.Round((decimal)t.Won * 100m / (t.Won + t.Lost), 2);
                return new WinRateOverTimePointDto(m.Key, rate);
            })
            .ToList();

        return new DashboardDto(tenderActivity, winLossByCategory, revenueByMonth, winRateOverTime, from, to);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Default period is the last 12 months (anchored to first-of-month). Both
    /// caller-provided dates are coerced to UTC and the half-open interval
    /// <c>[from, to)</c> is enforced.
    /// </summary>
    private static (DateTime From, DateTime To) NormalizePeriod(DateTime from, DateTime to)
    {
        if (from == default || to == default || from >= to)
        {
            var nowUtc = DateTime.UtcNow;
            to = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            from = to.AddMonths(-12);
        }
        from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        to   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);
        return (from, to);
    }

    /// <summary>Enumerate first-of-month UTC dates from <paramref name="from"/> up to (excluding) <paramref name="to"/>.</summary>
    private static List<(string Key, DateTime Start)> MonthsBetween(DateTime from, DateTime to)
    {
        var result = new List<(string, DateTime)>();
        var cursor = new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end    = new DateTime(to.Year,   to.Month,   1, 0, 0, 0, DateTimeKind.Utc);
        while (cursor < end)
        {
            result.Add((Key(cursor.Year, cursor.Month), cursor));
            cursor = cursor.AddMonths(1);
        }
        return result;
    }

    private static string Key(int year, int month) => $"{year:0000}-{month:00}";
}
