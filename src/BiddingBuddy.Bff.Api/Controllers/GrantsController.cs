using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Client-facing grant discovery. Proxies BiddingBuddyServices (MongoDB) exactly as the tender
/// read path does — the Postgres <c>grant_opportunities</c> row is a shadow INDEX for matching and
/// deep-links, not the read model.
/// </summary>
[ApiController]
[Route("api/grants")]
[Authorize]
[Produces("application/json")]
public class GrantsController(IGrantServicesClient grants) : BffControllerBase
{
    /// <summary>Paged grant search with eligibility and deadline filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedGrantListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Search([FromQuery] GrantSearchRequestDto query, CancellationToken ct)
    {
        var page  = await grants.SearchGrantsAsync(query, ct);
        var items = (page.Items ?? []).ToListDto();

        return Ok(new PagedGrantListDto(
            Items:           items,
            TotalCount:      page.TotalCount,
            Page:            page.Page,
            PageSize:        page.PageSize,
            TotalPages:      page.TotalPages,
            HasNextPage:     page.Page < page.TotalPages,
            HasPreviousPage: page.Page > 1));
    }

    /// <summary>
    /// Filter options present in the corpus, with counts, scoped to a status view.
    /// Lets the client populate its dropdowns from real data instead of a hardcoded vocabulary.
    /// </summary>
    [HttpGet("facets")]
    [ProducesResponseType(typeof(GrantFacetsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFacets([FromQuery] GrantFacetRequestDto query, CancellationToken ct)
    {
        var raw = await grants.GetGrantFacetsAsync(query, ct);

        return Ok(new GrantFacetsDto(
            Categories:     Map(raw.Categories),
            ApplicantTypes: Map(raw.ApplicantTypes),
            Agencies:       Map(raw.Agencies)));

        // Upstream values are nullable strings; a blank is not a selectable filter value, so it is
        // dropped rather than forwarded as an option that would filter to the empty string.
        static List<GrantFacetValueDto> Map(List<RawGrantFacetValueDto>? values) =>
            (values ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v.Value))
                .Select(v => new GrantFacetValueDto(v.Value!, v.Count))
                .ToList();
    }

    /// <summary>Full detail for one grant, by its GUID-shaped id.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(GrantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var raw = await grants.GetRawGrantAsync(id, ct);
        if (raw is null) return NotFound();

        // Null when the upstream id is not GUID-shaped. 404 rather than 500: an id we cannot parse
        // is indistinguishable, from the client's side, from one that does not exist.
        var detail = raw.ToDetailDto();
        return detail is null ? NotFound() : Ok(detail);
    }
}
