using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Admin CRUD over notification_templates.
/// Templates are global config (one row per logical event × channel), so this lives
/// under /internal and is protected by X-Api-Key — same posture as /internal/migrations.
/// </summary>
[ApiController]
[Route("internal/notification-templates")]
[PipelineApiKey]
[Produces("application/json")]
public class NotificationTemplatesController(INotificationTemplateService svc) : ControllerBase
{
    /// <summary>List templates, optionally filtered by code and/or channel.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List([FromQuery] string? code, [FromQuery] string? channel, CancellationToken ct)
        => Ok(await svc.ListAsync(code, channel, ct));

    /// <summary>Get one template by id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(await svc.GetAsync(id, ct));

    /// <summary>Create a new template. Unique on (code, channel).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateNotificationTemplateDto dto, CancellationToken ct)
    {
        var created = await svc.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Patch fields on an existing template.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNotificationTemplateDto dto, CancellationToken ct)
        => Ok(await svc.UpdateAsync(id, dto, ct));

    /// <summary>Delete a template. The processor caches by (code, channel) — it'll miss after this.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, ct);
        return NoContent();
    }
}
