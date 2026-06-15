using System.Text.Json;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// BFF's notification publisher (see handoff "What BFF owns").
///
/// On each call:
/// <list type="number">
///   <item>Inserts one <c>notifications</c> row + N <c>notification_deliveries</c> rows
///         (Pending, with category-derived <c>max_retries</c>) inside a single DB transaction.</item>
///   <item>After commit, publishes one thin trigger per delivery to <c>notification.{channel}</c>.
///         Message contains ONLY <c>DeliveryId</c>, <c>Channel</c>, <c>CorrelationId</c>.</item>
/// </list>
/// If RabbitMQ publish fails, the row stays Pending and the processor's pending-grace
/// poller will pick it up — BFF MUST NOT update the delivery row after insert.
/// </summary>
public class NotificationPublisher(
    BffDbContext db,
    IRabbitMqPublisher mq,
    ILogger<NotificationPublisher> log) : INotificationPublisher
{
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        NotificationCategory.Transactional,
        NotificationCategory.Information,
        NotificationCategory.Marketing,
    };

    private static readonly HashSet<string> AllowedChannels = new(StringComparer.Ordinal)
    {
        NotificationChannel.Email,
        NotificationChannel.Sms,
        NotificationChannel.WhatsApp,
        NotificationChannel.Firebase,
        NotificationChannel.InApp,
    };

    public async Task<NotificationDispatchResultDto> SendAsync(SendNotificationDto dto, CancellationToken ct = default)
    {
        Validate(dto);

        var maxRetries = MaxRetriesFor(dto.Category);
        var correlationId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        var payloadJson = JsonSerializer.Serialize(
            dto.Payload ?? new Dictionary<string, object>(),
            new JsonSerializerOptions { PropertyNamingPolicy = null });

        var notification = new Notification
        {
            Id            = notificationId,
            Category      = dto.Category,
            TemplateCode  = dto.TemplateCode,
            UserId        = dto.UserId,
            Payload       = payloadJson,
            CorrelationId = correlationId,
            CreatedAt     = DateTime.UtcNow,
        };

        var deliveries = dto.Recipients
            // Dedup same channel — the table has a UNIQUE (notification_id, channel) constraint.
            .GroupBy(r => r.Channel, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(r => new NotificationDelivery
            {
                Id               = Guid.NewGuid(),
                NotificationId   = notificationId,
                Channel          = r.Channel,
                RecipientAddress = r.RecipientAddress,
                Status           = "Pending",
                MaxRetries       = maxRetries,
                CreatedAt        = DateTime.UtcNow,
            })
            .ToList();

        // 1. DB transaction — insert event + deliveries together.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.Notifications.Add(notification);
            db.NotificationDeliveries.AddRange(deliveries);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        log.LogInformation(
            "Notification {NotificationId} ({Category}/{Template}) created with {Count} deliveries; correlationId={CorrelationId}",
            notificationId, dto.Category, dto.TemplateCode, deliveries.Count, correlationId);

        // 2. Publish thin triggers to RabbitMQ — one per delivery. Per-channel failures
        //    are tolerated: the processor's pending-grace poller will re-pick stragglers.
        var results = new List<DispatchedDeliveryDto>(deliveries.Count);
        foreach (var d in deliveries)
        {
            var queue = QueueFor(d.Channel);
            var trigger = new { DeliveryId = d.Id, Channel = d.Channel, CorrelationId = correlationId };
            var published = await mq.PublishAsync(queue, trigger, correlationId, ct);

            if (!published)
            {
                log.LogWarning(
                    "RabbitMQ publish failed for delivery {DeliveryId} (queue {Queue}); the processor's poller will pick it up after PendingGraceSeconds.",
                    d.Id, queue);
            }

            results.Add(new DispatchedDeliveryDto(d.Id, d.Channel, d.RecipientAddress, published));
        }

        return new NotificationDispatchResultDto(notificationId, correlationId, results);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void Validate(SendNotificationDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.Category) || !AllowedCategories.Contains(dto.Category))
            throw new ArgumentException(
                $"Category must be one of: {string.Join(", ", AllowedCategories)} (case-sensitive). Got: '{dto.Category}'.",
                nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.TemplateCode))
            throw new ArgumentException("TemplateCode is required.", nameof(dto));
        if (dto.Recipients is null || dto.Recipients.Count == 0)
            throw new ArgumentException("At least one recipient is required.", nameof(dto));

        foreach (var r in dto.Recipients)
        {
            if (string.IsNullOrWhiteSpace(r.Channel) || !AllowedChannels.Contains(r.Channel))
                throw new ArgumentException(
                    $"Channel must be one of: {string.Join(", ", AllowedChannels)} (case-sensitive). Got: '{r.Channel}'.",
                    nameof(dto));
            if (string.IsNullOrWhiteSpace(r.RecipientAddress))
                throw new ArgumentException($"RecipientAddress is required for channel {r.Channel}.", nameof(dto));
        }
    }

    /// <summary>Per-category retry budget — values come from the handoff.</summary>
    private static int MaxRetriesFor(string category) => category switch
    {
        NotificationCategory.Transactional => 5,
        NotificationCategory.Information   => 3,
        NotificationCategory.Marketing     => 1,
        _                                  => 5,
    };

    /// <summary>Channel → RabbitMQ queue name (lowercase, per the topology constants).</summary>
    private static string QueueFor(string channel) => channel switch
    {
        NotificationChannel.Email    => "notification.email",
        NotificationChannel.Sms      => "notification.sms",
        NotificationChannel.WhatsApp => "notification.whatsapp",
        NotificationChannel.Firebase => "notification.firebase",
        NotificationChannel.InApp    => "notification.inapp",
        _ => throw new ArgumentOutOfRangeException(nameof(channel), $"Unknown channel: {channel}"),
    };
}
