using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Time-fallback flush for tender-match digests. Count-triggered flushes happen
/// inline when a tender is upserted; this endpoint delivers whatever is still
/// buffered (below the digest threshold) and expires stale matches. Drive it on a
/// schedule (e.g. daily) with the pipeline X-Api-Key.
/// </summary>
[ApiController]
[Route("internal/digests")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalDigestsController(IMatchingService matching) : ControllerBase
{
    /// <summary>Flush every org with pending matches regardless of digest size.</summary>
    [HttpPost("flush")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Flush(CancellationToken ct)
    {
        var orgsDelivered = await matching.FlushAllDueAsync(ct);
        return Ok(new { orgsDelivered });
    }
}
