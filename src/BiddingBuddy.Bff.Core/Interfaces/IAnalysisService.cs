using BiddingBuddy.Bff.Core.DTOs.Analysis;
using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IAnalysisService
{
    Task<AiAnalysisResultDto?> GetTenderAnalysisAsync(Guid tenderId, Guid orgId, CancellationToken ct = default);
    Task<IReadOnlyList<PerformanceSnapshotDto>> GetPerformanceSnapshotsAsync(Guid orgId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Top KPI tiles on the Reports page. All counts/sums live-queried from
    /// <c>tenders</c> + <c>bids</c> (no snapshot table read).
    /// </summary>
    Task<KpisDto> GetKpisAsync(Guid orgId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>
    /// All four chart series for the Reports page in one round-trip. Months in the
    /// period are zero-filled, so the chart x-axis is always continuous even when
    /// nothing happened in a given month.
    /// </summary>
    Task<DashboardDto> GetDashboardAsync(Guid orgId, DateTime from, DateTime to, CancellationToken ct = default);
}
