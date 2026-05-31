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
}
