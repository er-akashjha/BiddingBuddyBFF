using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
[Produces("application/json")]
public class NotificationsController(INotificationService notificationService) : BffControllerBase
{
    /// <summary>Paginated notification list for the current user in the org.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await notificationService.ListAsync(CurrentOrgId, CurrentUserId, unreadOnly, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Mark a single notification as read.</summary>
    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notificationService.MarkReadAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Mark all unread notifications as read for the current user in this org.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notificationService.MarkAllReadAsync(CurrentOrgId, CurrentUserId, ct);
        return NoContent();
    }

    /// <summary>Get notification channel preferences for the current user.</summary>
    [HttpGet("preferences")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationPreferenceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var prefs = await notificationService.GetPreferencesAsync(CurrentUserId, ct);
        return Ok(prefs);
    }

    /// <summary>Update which channels and event types trigger notifications for the current user.</summary>
    [HttpPatch("preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesDto dto, CancellationToken ct)
    {
        await notificationService.UpdatePreferencesAsync(CurrentUserId, dto, ct);
        return NoContent();
    }
}
