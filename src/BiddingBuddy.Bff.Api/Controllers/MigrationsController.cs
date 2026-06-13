using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Admin;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Applies pending PostgreSQL schema migrations on demand.
/// Protected by X-Api-Key (Pipeline:ApiKey). Lives under /internal so it bypasses
/// the org-context middleware — it is an infrastructure operation, not org-scoped.
/// </summary>
[ApiController]
[Route("internal/migrations")]
[PipelineApiKey]
[Produces("application/json")]
public class MigrationsController(IDbMigrator migrator) : ControllerBase
{
    /// <summary>List all migration scripts and whether each has been applied.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MigrationStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await migrator.GetStatusAsync(ct);
        return Ok(status);
    }

    /// <summary>Run all not-yet-applied migration scripts. Idempotent — safe to call repeatedly.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MigrationRunResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Apply(CancellationToken ct)
    {
        var result = await migrator.ApplyPendingAsync(ct);
        return Ok(result);
    }
}
