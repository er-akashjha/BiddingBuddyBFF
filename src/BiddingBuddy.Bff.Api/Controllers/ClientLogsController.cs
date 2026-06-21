using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Sink for frontend (React SPA) error reports so client-side crashes and failed API
/// calls land in Loki next to backend logs instead of dying in the browser console.
/// Each entry is written through the BFF's Serilog pipeline, tagged <c>[client]</c> so
/// it is easy to filter, and carries the request's correlation id (the SPA sends
/// <c>X-Correlation-Id</c>, which the correlation middleware already threads into the
/// log context). Anonymous + IP rate-limited to deter abuse.
/// </summary>
[ApiController]
[Route("api/client-logs")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class ClientLogsController(ILogger<ClientLogsController> logger) : ControllerBase
{
    private const int MaxBatch = 50;
    private const int MaxMessageLength = 4000;

    /// <summary>Accepts a small batch of client log events. Returns 204 (best-effort, never fails the SPA).</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Ingest([FromBody] IReadOnlyList<ClientLogEntryDto>? entries)
    {
        if (entries is null || entries.Count == 0)
            return NoContent();

        foreach (var e in entries.Take(MaxBatch))
        {
            if (string.IsNullOrWhiteSpace(e.Message))
                continue;

            var message = e.Message.Length > MaxMessageLength
                ? e.Message[..MaxMessageLength]
                : e.Message;

            // Stack trace (when present) goes on its own line so the [client] prefix
            // stays greppable and the trace is still captured.
            var detail = string.IsNullOrWhiteSpace(e.Stack) ? message : $"{message}\n{e.Stack}";

            logger.Log(MapLevel(e.Level),
                "[client] {Message} (url={Url})", detail, e.Url ?? "n/a");
        }

        return NoContent();
    }

    private static LogLevel MapLevel(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "error" or "fatal" or "critical" => LogLevel.Error,
        "warn" or "warning"              => LogLevel.Warning,
        "info" or "information"          => LogLevel.Information,
        "debug" or "trace"               => LogLevel.Debug,
        _                                 => LogLevel.Warning,
    };
}

/// <summary>One client-side log event forwarded by the SPA.</summary>
public sealed record ClientLogEntryDto(string? Level, string? Message, string? Stack, string? Url);
