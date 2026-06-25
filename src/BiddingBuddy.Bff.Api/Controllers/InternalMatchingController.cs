using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// On-demand trigger for the tender-alert scan (the same logic the scheduled
/// <c>TenderMatchScanWorker</c> runs). API-key protected; handy for ops, testing,
/// or driving the scan from an external scheduler instead of the in-process worker.
/// </summary>
[ApiController]
[Route("internal/matching")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalMatchingController(IMatchingService matching) : ControllerBase
{
    /// <summary>
    /// Evaluate every not-yet-scanned tender against active interest rules and email
    /// one digest per matched org.
    /// </summary>
    /// <param name="backfill">
    /// When true, first re-arms ALL tenders (clears <c>alerts_scanned_at</c>) so existing
    /// matches are surfaced too. Use sparingly — it can email a large backlog.
    /// </param>
    /// <param name="batchSize">Tenders evaluated per DB round-trip (default 200).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpPost("scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Scan(
        [FromQuery] bool backfill = false,
        [FromQuery] int batchSize = 200,
        CancellationToken ct = default)
    {
        var result = await matching.ScanNewTendersAsync(batchSize, backfill, ct);
        return Ok(result);
    }
}
