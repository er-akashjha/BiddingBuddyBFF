using BiddingBuddy.Bff.Core.DTOs.Analysis;
using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IAnalysisService
{
    Task<AiAnalysisResultDto?> GetTenderAnalysisAsync(Guid tenderId, Guid orgId, CancellationToken ct = default);
    Task<IReadOnlyList<PerformanceSnapshotDto>> GetPerformanceSnapshotsAsync(Guid orgId, int limit, CancellationToken ct = default);
}
