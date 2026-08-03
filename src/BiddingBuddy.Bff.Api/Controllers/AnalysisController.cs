using BiddingBuddy.Bff.Core.Billing;
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
public class AnalysisController(
    IAnalysisService analysisService,
    IAiQuotaService aiQuota,
    IPlanService planService) : BffControllerBase
{
    /// <summary>
    /// Get AI analysis result for a specific tender (eligibility, risk, win strategy, bid range).
    /// This is the deliberate "view AI" action — it consumes one monthly AI credit the FIRST time
    /// an org opens a given tender's analysis in an IST calendar month (re-views are free; the
    /// tender-detail unlock shares the same key, so this never double-charges). Quota exhausted →
    /// 403 UPGRADE_REQUIRED. Returns 200 with a null <c>analysis</c> when the credit was consumed
    /// but no extended analysis row exists — the unlock still reveals the tender's AI fields.
    /// </summary>
    [HttpGet("tenders/{tenderId:guid}")]
    [ProducesResponseType(typeof(TenderAnalysisResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTenderAnalysis(Guid tenderId, CancellationToken ct)
    {
        var verdict = await aiQuota.TryConsumeAsync(
            CurrentOrgId, CurrentUserId, PlanFeatures.AiSummary, tenderId.ToString(), ct);
        if (!verdict.Allowed)
        {
            var plan = await planService.GetPlanForAsync(CurrentOrgId, ct);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error        = "You've used all AI summaries included in your plan this month.",
                code         = "UPGRADE_REQUIRED",
                feature      = PlanFeatures.AiSummary,
                requiredPlan = PlanCatalog.NextPlanUp(plan.PlanCode),
                currentPlan  = plan.PlanCode,
                used         = verdict.Used,
                quota        = verdict.Quota,
            });
        }

        var analysis = await analysisService.GetTenderAnalysisAsync(tenderId, CurrentOrgId, ct);

        // The eligibility verdict is a Growth+ feature even once unlocked.
        var effective = await planService.GetPlanForAsync(CurrentOrgId, ct);
        if (analysis is not null && !effective.HasFeature(PlanFeatures.EligibilityCheck))
            analysis = analysis with { EligibilityBreakdown = null };

        return Ok(new TenderAnalysisResponseDto(analysis, new AiUsageDto(verdict.Used, verdict.Quota)));
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
