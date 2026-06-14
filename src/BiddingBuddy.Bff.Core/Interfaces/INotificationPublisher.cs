using BiddingBuddy.Bff.Core.DTOs.Notifications;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Outbound notification publisher.
///
/// Insert-then-publish:
/// <list type="number">
///   <item>One row into <c>notifications</c> (logical event + payload + correlation id).</item>
///   <item>One row per recipient into <c>notification_deliveries</c> (Pending, with per-category max_retries).</item>
///   <item>One thin RabbitMQ message per delivery → <c>notification.{channel}</c> queue.</item>
/// </list>
/// If RabbitMQ publishing fails, the row stays Pending; the processor's poller
/// picks it up after its PendingGraceSeconds window. BFF MUST NOT touch the
/// delivery row after insert — every state column is processor-owned.
/// </summary>
public interface INotificationPublisher
{
    Task<NotificationDispatchResultDto> SendAsync(SendNotificationDto dto, CancellationToken ct = default);
}
