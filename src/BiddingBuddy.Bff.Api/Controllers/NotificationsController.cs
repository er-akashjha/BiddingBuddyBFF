using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(INotificationService notificationService) : BffControllerBase
{
    /// <summary>GET /api/notifications?unreadOnly=false&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await notificationService.ListAsync(CurrentOrgId, CurrentUserId, unreadOnly, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>PATCH /api/notifications/{id}/read</summary>
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notificationService.MarkReadAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>POST /api/notifications/read-all</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notificationService.MarkAllReadAsync(CurrentOrgId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>GET /api/notifications/preferences</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var prefs = await notificationService.GetPreferencesAsync(CurrentUserId, ct);
        return Ok(prefs);
    }

    /// <summary>PATCH /api/notifications/preferences</summary>
    [HttpPatch("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesDto dto, CancellationToken ct)
    {
        await notificationService.UpdatePreferencesAsync(CurrentUserId, dto, ct);
        return NoContent();
    }
}
