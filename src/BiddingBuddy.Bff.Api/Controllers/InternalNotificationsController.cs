using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Internal trigger endpoint for the notification publisher.
///
/// For notifications that originate OUTSIDE the BFF (e.g. BidProcessor decides
/// to notify users of a tender match, or an admin tool wants to broadcast).
/// In-BFF flows (signup, org invite) call <see cref="INotificationPublisher"/>
/// directly from their service — no HTTP hop.
///
/// Same posture as <c>/internal/migrations</c> and <c>/internal/notification-templates</c>:
/// API-key auth, bypasses org middleware. Lives under /internal so the BidProcessor
/// can call it with the existing X-Api-Key header.
/// </summary>
[ApiController]
[Route("internal/notifications")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalNotificationsController(INotificationPublisher publisher) : ControllerBase
{
    /// <summary>
    /// Publish a notification: inserts the event + per-channel deliveries (Pending),
    /// then publishes thin triggers to RabbitMQ. Idempotency note: this endpoint
    /// always inserts a new event row — callers must dedup on their side (e.g. by
    /// recording a processed-marker for the upstream event).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(NotificationDispatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Send([FromBody] SendNotificationDto dto, CancellationToken ct)
    {
        var result = await publisher.SendAsync(dto, ct);
        return Ok(result);
    }
}
