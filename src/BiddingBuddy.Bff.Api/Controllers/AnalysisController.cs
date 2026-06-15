using BiddingBuddy.Bff.Core.DTOs.Analysis;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/analysis")]
[Authorize]
[Produces("application/json")]
public class AnalysisController(IAnalysisService analysisService) : BffControllerBase
{
    /// <summary>Get AI analysis result for a specific tender (eligibility, risk, win strategy, bid range).</summary>
    [HttpGet("tenders/{tenderId:guid}")]
    [ProducesResponseType(typeof(AiAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenderAnalysis(Guid tenderId, CancellationToken ct)
    {
        var analysis = await analysisService.GetTenderAnalysisAsync(tenderId, CurrentOrgId, ct);
        if (analysis is null) return NotFound(new { error = "No AI analysis available for this tender." });
        return Ok(analysis);
    }

    /// <summary>Get monthly performance snapshots for the org (win rate, bid values, top categories).</summary>
    [HttpGet("performance")]
    [ProducesResponseType(typeof(IReadOnlyList<PerformanceSnapshotDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPerformance([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        var snapshots = await analysisService.GetPerformanceSnapshotsAsync(CurrentOrgId, limit, ct);
        return Ok(snapshots);
    }

    /// <summary>
    /// Top KPI tiles on the Reports page. Defaults to the last 12 months when
    /// <c>from</c>/<c>to</c> are omitted. Half-open interval [from, to). UTC.
    /// </summary>
    [HttpGet("kpis")]
    [ProducesResponseType(typeof(KpisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKpis(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null,
        CancellationToken ct       = default)
    {
        var kpis = await analysisService.GetKpisAsync(
            CurrentOrgId, from ?? default, to ?? default, ct);
        return Ok(kpis);
    }

    /// <summary>
    /// Full Reports-page dashboard — Tender Activity Trend, Win/Loss by Category,
    /// Revenue Won monthly, and Win Rate Over Time in one round-trip. Empty months
    /// are zero-filled so the chart x-axis stays continuous.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to   = null,
        CancellationToken ct       = default)
    {
        var dashboard = await analysisService.GetDashboardAsync(
            CurrentOrgId, from ?? default, to ?? default, ct);
        return Ok(dashboard);
    }
}
