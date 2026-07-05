using BiddingBuddy.Bff.Core.DTOs.SavedFilters;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Per-user saved tender filters. Org-scoped (Bearer JWT + X-Org-Id). Backs the
/// Tenders page's auto-restore of last-used filters plus named saved views.
/// </summary>
[ApiController]
[Route("api/saved-filters")]
[Authorize]
[Produces("application/json")]
public class SavedFiltersController(ISavedFilterService service) : BffControllerBase
{
    /// <summary>The current user's last-used snapshot (if any) plus their named views.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SavedFiltersResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await service.GetAsync(CurrentOrgId, CurrentUserId, ct));

    /// <summary>Upsert the single "last-used" snapshot for the current user + org.</summary>
    [HttpPut("last-used")]
    [ProducesResponseType(typeof(SavedFilterDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PutLastUsed([FromBody] PutLastUsedFilterDto dto, CancellationToken ct)
        => Ok(await service.UpsertLastUsedAsync(CurrentOrgId, CurrentUserId, dto.Filters, ct));

    /// <summary>Create a named saved view.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SavedFilterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateNamed([FromBody] CreateNamedFilterDto dto, CancellationToken ct)
    {
        var created = await service.CreateNamedAsync(CurrentOrgId, CurrentUserId, dto.Name, dto.Filters, ct);
        return StatusCode(201, created);
    }

    /// <summary>Delete one of the current user's saved filters by id.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, CurrentOrgId, CurrentUserId, ct);
        return NoContent();
    }
}
