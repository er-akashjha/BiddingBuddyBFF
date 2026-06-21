using BiddingBuddy.Bff.Infrastructure.Logging;
using Serilog.Context;

namespace BiddingBuddy.Bff.Api.Middleware;

/// <summary>
/// Reads the <c>X-Correlation-Id</c> header from each inbound request (or generates a
/// new GUID) and makes it available two ways for the duration of the request:
///   • pushed into Serilog's <see cref="LogContext"/> so every log line carries it;
///   • set on the ambient <see cref="CorrelationContext"/> so <see cref="CorrelationHeaderHandler"/>
///     forwards it on outbound calls to BiddingBuddyServices.
/// The id is echoed back on the response so the SPA can record it for debugging.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd(HeaderName, correlationId);
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (CorrelationContext.BeginScope(correlationId))
        {
            await _next(context);
        }
    }
}
