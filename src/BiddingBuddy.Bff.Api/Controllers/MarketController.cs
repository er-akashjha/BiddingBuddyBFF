using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Market intelligence derived from tender award results (aggregate, generic — not org-scoped).
/// Proxied from BiddingBuddyServices' Mongo aggregation (cached there).
///
/// Every number here is built from GeM's published award ladders. Two labelling rules apply to
/// anything a caller renders (see docs/tender-results/UI-FEATURES.md §4.4):
///   • prices are the sellers' OFFERED bids, never the accepted/contract value;
///   • the winner is INFERRED (lowest qualified), so its confidence must travel with it.
/// </summary>
[ApiController]
[Route("api/market")]
[Authorize]
[Produces("application/json")]
public class MarketController(
    IBiddingBuddyServicesClient servicesClient,
    IOrganizationService organizations) : BffControllerBase
{
    /// <summary>Aggregate winning-price / competition stats over awards for a slice —
    /// median + quartile band, participants, L1↔L2 spread, MSE win rate, and the sample size.</summary>
    [HttpGet("pricing")]
    [ProducesResponseType(typeof(MarketPricingStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPricing([FromQuery] MarketFilterDto filter, CancellationToken ct)
        => Ok(await servicesClient.GetMarketPricingAsync(filter, ct));

    /// <summary>Winning-price stats bucketed by <paramref name="groupBy"/>
    /// (category | state | month | seller | buyer) — the series behind the market charts.</summary>
    [HttpGet("grouped")]
    [ProducesResponseType(typeof(List<MarketGroupBucketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGrouped(
        [FromQuery] MarketFilterDto filter,
        [FromQuery] string groupBy = "category",
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await servicesClient.GetMarketGroupedAsync(filter, groupBy, limit, ct));

    /// <summary>Top winning sellers for the slice.</summary>
    [HttpGet("sellers")]
    [ProducesResponseType(typeof(List<SellerStatsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopSellers(
        [FromQuery] MarketFilterDto filter, [FromQuery] int limit = 20, CancellationToken ct = default)
        => Ok(await servicesClient.GetTopSellersAsync(filter, limit, ct));

    /// <summary>Award history for one seller.</summary>
    [HttpGet("sellers/profile")]
    [ProducesResponseType(typeof(SellerStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSellerProfile([FromQuery] string seller, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(seller)) return BadRequest(new { error = "seller is required" });
        var stats = await servicesClient.GetSellerStatsAsync(seller, ct);
        return stats is null ? NotFound() : Ok(stats);
    }

    /// <summary>
    /// Head-to-head: how the CALLER'S org has fared against every rival it has shared a ladder with.
    /// Uses the org's configured GeM seller name (falling back to its org name) as our identity, so
    /// it 404s with a clear code until that's set rather than silently returning an empty record.
    /// </summary>
    [HttpGet("head-to-head")]
    [ProducesResponseType(typeof(List<HeadToHeadRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetHeadToHead(
        [FromQuery] MarketFilterDto filter, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var identity = await ResolveOrgSellerIdentityAsync(ct);
        if (string.IsNullOrWhiteSpace(identity))
            return Conflict(new { error = "SELLER_IDENTITY_NOT_SET", message = "Set your GeM seller name in Settings to see head-to-head records." });

        return Ok(await servicesClient.GetHeadToHeadAsync(identity, filter, limit, ct));
    }

    /// <summary>Head-to-head for an explicitly named seller (not necessarily the caller's org).</summary>
    [HttpGet("head-to-head/{seller}")]
    [ProducesResponseType(typeof(List<HeadToHeadRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHeadToHeadFor(
        string seller,
        [FromQuery] MarketFilterDto filter,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await servicesClient.GetHeadToHeadAsync(seller, filter, limit, ct));

    /// <summary>Award behaviour for one buyer, including supplier concentration.</summary>
    [HttpGet("buyer")]
    [ProducesResponseType(typeof(BuyerProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBuyerProfile([FromQuery] string buyer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyer)) return BadRequest(new { error = "buyer is required" });
        var profile = await servicesClient.GetBuyerProfileAsync(buyer, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Comparable past awards for a live tender — "what did tenders like this go for".</summary>
    [HttpGet("comparables")]
    [ProducesResponseType(typeof(List<TenderResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComparables(
        [FromQuery] string? category,
        [FromQuery] string? state,
        [FromQuery] decimal? estimatedValue,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
        => Ok(await servicesClient.GetComparableAwardsAsync(category, state, estimatedValue, limit, ct));

    /// <summary>
    /// The org's seller identity on GeM ladders: the explicit setting, else the org name as a
    /// zero-config fallback (mirrors how award resolution matches bids).
    /// </summary>
    private async Task<string?> ResolveOrgSellerIdentityAsync(CancellationToken ct)
    {
        var org = await organizations.GetAsync(CurrentOrgId, CurrentUserId, ct);
        return !string.IsNullOrWhiteSpace(org.GemSellerName) ? org.GemSellerName : org.Name;
    }
}
