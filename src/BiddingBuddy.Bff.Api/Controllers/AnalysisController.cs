using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Analysis;
using BiddingBuddy.Bff.Core.DTOs.Fit;
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
    ITenderFitService tenderFit,
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
        var resourceId = tenderId.ToString();

        // Already unlocked this month → serve it without touching the meter. Checked first
        // so a re-read stays free even when the quota is now exhausted.
        var alreadyUnlocked = await aiQuota.IsUnlockedAsync(
            CurrentOrgId, PlanFeatures.AiSummary, resourceId, ct);

        // Fetch BEFORE consuming. This throws KeyNotFoundException → 404 for a tender that
        // does not exist, and consuming first would bill a credit for that 404 — on the Free
        // plan that is one of three for the month, spent on nothing. Any upstream failure
        // has the same shape, so the credit must be the last thing we take, not the first.
        var analysis = await analysisService.GetTenderAnalysisAsync(tenderId, CurrentOrgId, ct);

        AiQuotaVerdict verdict;
        // An empty result is not a product. Nothing has ever written ai_analysis_results, so
        // this was every request: consume, return null, and let the client render invented
        // filler over the top. Leave the meter alone and say so — `charged: false` is what
        // lets the client show "no analysis available" instead of billing for the sentence.
        if (alreadyUnlocked || analysis is null)
        {
            var (used, quota) = await aiQuota.GetUsageAsync(CurrentOrgId, PlanFeatures.AiSummary, ct);
            verdict = new AiQuotaVerdict(Allowed: true, AlreadyUnlocked: true, used, quota);
        }
        else
        {
            verdict = await aiQuota.TryConsumeAsync(
                CurrentOrgId, CurrentUserId, PlanFeatures.AiSummary, resourceId, ct);
        }

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

        // The eligibility verdict is a Growth+ feature even once unlocked.
        var effective = await planService.GetPlanForAsync(CurrentOrgId, ct);
        if (analysis is not null && !effective.HasFeature(PlanFeatures.EligibilityCheck))
            analysis = analysis with { EligibilityBreakdown = null };

        return Ok(new TenderAnalysisResponseDto(
            analysis, new AiUsageDto(verdict.Used, verdict.Quota), Charged: !verdict.AlreadyUnlocked));
    }

    /// <summary>
    /// <b>The bid-fit verdict — can THIS org bid THIS tender, and what will it tie up.</b>
    ///
    /// <para>Computed by a deterministic, versioned, cited rules engine
    /// (<c>Core/Fit/TenderFitRules.cs</c>) over the tender's structured extraction and the org's
    /// capability profile. No model decides a verdict; the optional narrative is written over one.
    /// Every finding carries the source that produced it and a confidence, both rendered.</para>
    ///
    /// <para>Charging: one credit when a real verdict is newly produced. Free for a re-read, for
    /// a tender already unlocked this month (shared key with the tender detail), for a re-run
    /// forced by a corrigendum, and for <c>insufficient_data</c>. Quota exhausted →
    /// 403 UPGRADE_REQUIRED via <c>PlanLimitException</c>.</para>
    /// </summary>
    /// <param name="rerun">Recompute even if a current report exists.</param>
    [HttpGet("tenders/{tenderId:guid}/fit")]
    [ProducesResponseType(typeof(FitReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenderFit(
        Guid tenderId, CancellationToken ct, [FromQuery] bool rerun = false)
        => Ok(await tenderFit.GetOrComputeAsync(CurrentOrgId, CurrentUserId, tenderId, rerun, ct));

    /// <summary>
    /// The stored verdict, or 204 if the org has never run one. Never computes and never meters,
    /// so a surface that merely wants to show a badge it already owns — the tender list, a bid
    /// card — cannot accidentally bill a customer for a page view.
    /// </summary>
    [HttpGet("tenders/{tenderId:guid}/fit/existing")]
    [ProducesResponseType(typeof(FitReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetExistingTenderFit(Guid tenderId, CancellationToken ct)
    {
        var report = await tenderFit.GetExistingAsync(CurrentOrgId, tenderId, ct);
        return report is null ? NoContent() : Ok(report);
    }

    /// <summary>
    /// The stored verdict as a .docx bid/no-bid note — the document a bid manager forwards to
    /// whoever approves chasing the tender. Exports only; never computes, so "Download" cannot
    /// become a second, invisible way to spend a credit. 404 when nothing has been run.
    /// </summary>
    [HttpGet("tenders/{tenderId:guid}/fit/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportTenderFit(Guid tenderId, CancellationToken ct)
    {
        var doc = await tenderFit.ExportAsync(CurrentOrgId, tenderId, ct);
        return File(doc.Content, FitDocumentResult.DocxContentType, doc.FileName);
    }

    /// <summary>
    /// Copy the report's blockers and gaps onto a bid's checklist, so the analysis becomes work
    /// the team ticks off rather than a page read once and closed. Idempotent on the item title:
    /// re-running after a corrigendum tops the list up instead of duplicating what is already done.
    /// </summary>
    [HttpPost("tenders/{tenderId:guid}/fit/push-to-bid/{bidId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PushFitToChecklist(Guid tenderId, Guid bidId, CancellationToken ct)
    {
        var added = await tenderFit.PushToChecklistAsync(CurrentOrgId, bidId, tenderId, ct);
        return Ok(new { added });
    }

    /// <summary>
    /// The org's AI meter for the current IST month, spending nothing. The client shows this
    /// beside the unlock action so the price of a click is visible before the click.
    /// </summary>
    [HttpGet("ai-usage")]
    [ProducesResponseType(typeof(AiUsageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiUsage(CancellationToken ct)
    {
        var (used, quota) = await aiQuota.GetUsageAsync(CurrentOrgId, PlanFeatures.AiSummary, ct);
        return Ok(new AiUsageDto(used, quota));
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
