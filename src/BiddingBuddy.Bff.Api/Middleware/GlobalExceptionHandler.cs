using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            KeyNotFoundException        => (StatusCodes.Status404NotFound,    "Not Found"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden,   "Forbidden"),
            ArgumentException           => (StatusCodes.Status400BadRequest,  "Bad Request"),
            InvalidOperationException   => (StatusCodes.Status400BadRequest,  "Bad Request"),
            _                          => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        // Log every handled failure, not just 500s — 4xx (auth, validation, not-found)
        // were previously silent, hiding the most user-visible failures.
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Request failed → {Status} {Title}", status, title);
        else
            logger.LogWarning(ex, "Request rejected → {Status} {Title}: {Detail}", status, title, ex.Message);

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title  = title,
                Detail = ex.Message,
            }, ct);

        return true;
    }
}
