using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Market intelligence derived from tender award results (aggregate, generic — not org-scoped).
/// Proxied from BiddingBuddyServices' Mongo aggregation (cached there).
/// </summary>
[ApiController]
[Route("api/market")]
[Authorize]
[Produces("application/json")]
public class MarketController(IBiddingBuddyServicesClient servicesClient) : ControllerBase
{
    /// <summary>Aggregate winning-price / competition stats over awards, optionally sliced by
    /// category and/or state (avg/min/max winning value, avg participants, avg L1↔L2 spread).</summary>
    [HttpGet("pricing")]
    [ProducesResponseType(typeof(MarketPricingStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPricing(
        [FromQuery] string? category, [FromQuery] string? state, CancellationToken ct)
    {
        var stats = await servicesClient.GetMarketPricingAsync(category, state, ct);
        return Ok(stats);
    }
}
