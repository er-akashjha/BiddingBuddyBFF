using System.Linq;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Anonymous, read-only tender browsing for logged-out "guest" visitors.
/// Serves the shared tender corpus from BiddingBuddyServices (MongoDB) with every
/// org-specific and AI-scoring field stripped (see <see cref="PublicTenderDetailDto"/>).
/// Requires no JWT and no X-Org-Id — the underlying Mongo query is org-agnostic.
/// IP rate-limited via the "public" policy to deter scraping.
/// </summary>
[ApiController]
[Route("api/public/tenders")]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Produces("application/json")]
public class PublicTendersController(IBiddingBuddyServicesClient servicesClient) : ControllerBase
{
    /// <summary>Public tender list (intrinsic fields only). Only provided filters are forwarded.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<PublicTenderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        var result = await servicesClient.SearchTendersAsync(query, ct);
        return Ok(result.Select(t => t.ToPublic()).ToList());
    }

    /// <summary>Paged public tender list (intrinsic fields only). Forwards pagination metadata.</summary>
    [HttpGet("paged")]
    [ProducesResponseType(typeof(PublicPagedTenderListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPaged([FromQuery] TenderSearchQueryDto query, CancellationToken ct)
    {
        var result = await servicesClient.SearchTendersPagedAsync(query, ct);
        return Ok(result.ToPublic());
    }

    /// <summary>Full public tender detail by ID (intrinsic fields only). 404 if not found.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PublicTenderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // GetTenderAsync throws InvalidOperationException on an upstream 404, and the
        // translator can throw FormatException on a non-GUID Mongo id — map both to a
        // clean 404 rather than leaking a 500 to anonymous callers.
        TenderDetailDto tender;
        try
        {
            tender = await servicesClient.GetTenderAsync(id.ToString(), ct);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (FormatException)
        {
            return NotFound();
        }

        return Ok(tender.ToPublic());
    }
}
