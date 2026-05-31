using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/analysis")]
[Authorize]
public class AnalysisController(IAnalysisService analysisService) : BffControllerBase
{
    /// <summary>GET /api/analysis/tenders/{tenderId}</summary>
    [HttpGet("tenders/{tenderId:guid}")]
    public async Task<IActionResult> GetTenderAnalysis(Guid tenderId, CancellationToken ct)
    {
        var analysis = await analysisService.GetTenderAnalysisAsync(tenderId, CurrentOrgId, ct);
        if (analysis is null) return NotFound(new { error = "No AI analysis available for this tender." });
        return Ok(analysis);
    }

    /// <summary>GET /api/analysis/performance?limit=12</summary>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        var snapshots = await analysisService.GetPerformanceSnapshotsAsync(CurrentOrgId, limit, ct);
        return Ok(snapshots);
    }
}
