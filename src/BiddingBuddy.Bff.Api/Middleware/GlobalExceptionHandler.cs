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
            InvalidOperationException   => (StatusCodes.Status400BadRequest,  "Bad Request"),
            _                          => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

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
