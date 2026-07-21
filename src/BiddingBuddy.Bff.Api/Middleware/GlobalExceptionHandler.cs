using BiddingBuddy.Bff.Core.Exceptions;
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
            // Before UpstreamServiceException existed, a failure inside BiddingBuddyServices
            // arrived here as InvalidOperationException and was reported to the caller as
            // 400 Bad Request — blaming the client for a server-side fault.
            UpstreamServiceException    => (StatusCodes.Status502BadGateway,  "Bad Gateway"),
            InvalidOperationException   => (StatusCodes.Status400BadRequest,  "Bad Request"),
            _                          => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        // Log every handled failure, not just 500s — 4xx (auth, validation, not-found)
        // were previously silent, hiding the most user-visible failures.
        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Request failed → {Status} {Title}", status, title);
        else
            logger.LogWarning(ex, "Request rejected → {Status} {Title}: {Detail}", status, title, ex.Message);

        if (ctx.Response.HasStarted)
        {
            // The response is already on the wire — setting the status or writing a body now
            // throws, ExceptionHandlerMiddleware logs "An exception was thrown attempting to
            // execute the error handler", and rethrows the original, which Kestrel turns into
            // a bare 500. Returning false lets that path proceed without the extra noise; the
            // log above is the record.
            logger.LogWarning("Response already started — cannot write the error body for {Path}", ctx.Request.Path);
            return false;
        }

        ctx.Response.StatusCode = status;

        // CancellationToken.None, NOT the `ct` passed in — that is HttpContext.RequestAborted.
        // When a client gave up mid-request (exactly what happens on these slow upstream
        // failures) the token is already cancelled, WriteAsJsonAsync threw from inside the
        // handler, and the intended 400/502 was lost as a bare 500. Writing the body is
        // best-effort cleanup and must not inherit the caller's cancellation.
        await ctx.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title  = title,
                Detail = ex.Message,
            }, CancellationToken.None);

        return true;
    }
}
