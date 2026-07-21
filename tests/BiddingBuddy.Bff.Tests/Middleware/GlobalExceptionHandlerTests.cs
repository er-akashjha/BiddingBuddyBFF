using System.Text.Json;
using BiddingBuddy.Bff.Api.Middleware;
using BiddingBuddy.Bff.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiddingBuddy.Bff.Tests.Middleware;

/// <summary>
/// Covers the status mapping and the two failure modes that turned handled 4xx/502s into
/// bare 500s in production: a cancelled request token, and a response already on the wire.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static GlobalExceptionHandler BuildHandler() =>
        new(NullLogger<GlobalExceptionHandler>.Instance);

    private static DefaultHttpContext BuildContext(out MemoryStream body)
    {
        body = new MemoryStream();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = body;
        ctx.Request.Path  = "/api/tenders/paged";
        return ctx;
    }

    private static ProblemDetails? ReadProblem(MemoryStream body)
    {
        body.Position = 0;
        return JsonSerializer.Deserialize<ProblemDetails>(
            body.ToArray(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    // ── status mapping ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpstreamServiceException_maps_to_502_not_400()
    {
        // Regression guard: this used to arrive as InvalidOperationException and be
        // reported as 400 Bad Request, blaming the caller for a BiddingBuddyServices fault.
        var ctx = BuildContext(out var body);
        var ex  = new UpstreamServiceException(
            "BiddingBuddyServices", "BiddingBuddyServices returned 500: boom", 500);

        var handled = await BuildHandler().TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status502BadGateway, ctx.Response.StatusCode);
        Assert.Equal("Bad Gateway", ReadProblem(body)?.Title);
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException),        StatusCodes.Status404NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), StatusCodes.Status403Forbidden)]
    [InlineData(typeof(ArgumentException),           StatusCodes.Status400BadRequest)]
    [InlineData(typeof(InvalidOperationException),   StatusCodes.Status400BadRequest)]
    [InlineData(typeof(Exception),                   StatusCodes.Status500InternalServerError)]
    public async Task Existing_mappings_are_unchanged(Type exceptionType, int expected)
    {
        var ctx = BuildContext(out _);
        var ex  = (Exception)Activator.CreateInstance(exceptionType)!;

        await BuildHandler().TryHandleAsync(ctx, ex, CancellationToken.None);

        Assert.Equal(expected, ctx.Response.StatusCode);
    }

    // ── cancelled caller ─────────────────────────────────────────────────────

    [Fact]
    public async Task Writes_the_error_body_even_when_the_caller_has_disconnected()
    {
        // The handler used to pass HttpContext.RequestAborted straight to
        // WriteAsJsonAsync. On a slow upstream failure the SPA often gave up first, so the
        // token was already cancelled, the write threw from inside the handler, and
        // ExceptionHandlerMiddleware rethrew the original — surfacing as a bare 500.
        var ctx = BuildContext(out var body);
        var cancelled = new CancellationToken(canceled: true);

        var handled = await BuildHandler().TryHandleAsync(
            ctx, new UpstreamServiceException("BiddingBuddyServices", "upstream down"), cancelled);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status502BadGateway, ctx.Response.StatusCode);
        Assert.Equal("Bad Gateway", ReadProblem(body)?.Title);
    }

    // ── response already started ─────────────────────────────────────────────

    [Fact]
    public async Task Declines_without_throwing_when_the_response_has_already_started()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/tenders/paged";
        ctx.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        // Must not throw: setting the status or writing a body after the response has
        // started is what produced "An exception was thrown attempting to execute the
        // error handler". Returning false lets the framework's own path take over.
        var handled = await BuildHandler().TryHandleAsync(
            ctx, new InvalidOperationException("late failure"), CancellationToken.None);

        Assert.False(handled);
    }

    /// <summary>Minimal response feature that reports the response as already begun.</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream               Body        { get; set; } = Stream.Null;
        public bool                 HasStarted  => true;
        public IHeaderDictionary    Headers     { get; set; } = new HeaderDictionary();
        public string?              ReasonPhrase { get; set; }
        public int                  StatusCode  { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }
}
