namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Thin RabbitMQ producer used by the notification publisher.
/// Implementations declare the target queue idempotently and publish a
/// JSON-serialized payload as a persistent message.
/// </summary>
public interface IRabbitMqPublisher
{
    /// <summary>
    /// Publish <paramref name="message"/> as JSON to <paramref name="queueName"/>.
    /// Returns true on success, false on transport failure (caller decides whether
    /// to surface the error — for notifications we log + let the processor's
    /// pending-grace poller self-heal).
    /// </summary>
    Task<bool> PublishAsync<T>(string queueName, T message, Guid? correlationId = null, CancellationToken ct = default);
}
