using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// On-demand triggers for the notification scans (the same logic the scheduled
/// <c>DeadlineScanWorker</c> / <c>WeeklyDigestWorker</c> run). API-key protected; handy for
/// ops, testing, or driving from an external scheduler instead of the in-process workers.
/// </summary>
[ApiController]
[Route("internal")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalRemindersController(
    IDeadlineScanService deadlineScan,
    IWeeklyDigestService weeklyDigest) : ControllerBase
{
    /// <summary>Scan bids/invoices/compliance/delivery/EMD for due/overdue/expiring and send reminders.</summary>
    [HttpPost("deadline/scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RunDeadlineScan(CancellationToken ct)
        => Ok(await deadlineScan.RunAsync(ct));

    /// <summary>Send this week's org summary digest to any org that has not received it yet.</summary>
    [HttpPost("weekly-digest/run")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RunWeeklyDigest(CancellationToken ct)
        => Ok(new { sent = await weeklyDigest.RunAsync(ct) });
}
