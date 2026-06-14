using System.Text.Json;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Singleton RabbitMQ publisher.
///
/// Holds a single <see cref="IConnection"/> for the process and creates a short-lived
/// channel per publish (channels are cheap; connections are not). Declares the target
/// queue durably + bound to <c>bid.dlx</c> on each publish — declarations are idempotent,
/// so it's safe even if the queue already exists.
/// </summary>
public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqPublisher> _log;
    private readonly Lazy<IConnection> _connection;
    private readonly object _connLock = new();
    private bool _disposed;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> opts, ILogger<RabbitMqPublisher> log)
    {
        _opts = opts.Value;
        _log  = log;
        _connection = new Lazy<IConnection>(CreateConnection, isThreadSafe: true);
    }

    private IConnection CreateConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName               = _opts.HostName,
            Port                   = _opts.Port,
            UserName               = _opts.Username,
            Password               = _opts.Password,
            VirtualHost            = _opts.VirtualHost,
            ClientProvidedName     = _opts.ClientName,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval  = TimeSpan.FromSeconds(5),
        };
        _log.LogInformation("Opening RabbitMQ connection to {Host}:{Port} vhost={VHost}",
            _opts.HostName, _opts.Port, _opts.VirtualHost);
        return factory.CreateConnection();
    }

    public Task<bool> PublishAsync<T>(string queueName, T message, Guid? correlationId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queueName)) throw new ArgumentException("queueName required", nameof(queueName));
        if (message is null) throw new ArgumentNullException(nameof(message));

        try
        {
            using var channel = _connection.Value.CreateModel();

            // The queue is OWNED by the BidProcessor team — they declare it with
            // x-dead-letter-exchange + x-dead-letter-routing-key. If we re-declare
            // here with different arguments, RabbitMQ raises PRECONDITION_FAILED
            // (channel error 406) and the publish silently drops on the closed
            // channel. Passive declare verifies the queue exists without touching
            // arguments — and gives us a clean error if someone misconfigures.
            channel.QueueDeclarePassive(queueName);

            var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOpts);

            var props = channel.CreateBasicProperties();
            props.Persistent  = true;
            props.ContentType = "application/json";
            if (correlationId.HasValue)
                props.CorrelationId = correlationId.Value.ToString();

            channel.BasicPublish(
                exchange:   string.Empty,        // default exchange — routes by queue name
                routingKey: queueName,
                basicProperties: props,
                body: body);

            _log.LogDebug("Published to {Queue} ({Bytes} bytes), correlationId={CorrelationId}",
                queueName, body.Length, correlationId);
            return Task.FromResult(true);
        }
        catch (BrokerUnreachableException ex)
        {
            _log.LogError(ex, "RabbitMQ broker unreachable; could not publish to {Queue}", queueName);
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to publish to {Queue}", queueName);
            return Task.FromResult(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection.IsValueCreated)
        {
            try { _connection.Value.Close(); } catch { /* ignore on shutdown */ }
            _connection.Value.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
