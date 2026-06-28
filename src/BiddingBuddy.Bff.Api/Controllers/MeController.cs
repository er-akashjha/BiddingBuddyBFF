using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
[Produces("application/json")]
public class MeController(IBidService bidService) : BffControllerBase
{
    /// <summary>
    /// The current user's checklist tasks across all bids in the org, each bucketed
    /// (overdue|today|week|later|done). Filter with <c>status</c> = open|done (default: all).
    /// Backs the "My work" view.
    /// </summary>
    [HttpGet("tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<MyTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTasks([FromQuery] string? status, CancellationToken ct)
    {
        var tasks = await bidService.GetMyTasksAsync(CurrentOrgId, CurrentUserId, status, ct);
        return Ok(tasks);
    }
}
