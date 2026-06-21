using System.Net.Http;

namespace BiddingBuddy.Bff.Infrastructure.Logging;

/// <summary>
/// <see cref="DelegatingHandler"/> that reads the ambient <see cref="CorrelationContext"/>
/// and adds an <c>X-Correlation-Id</c> header to every outbound HTTP request, so the
/// correlation id set by the BFF's middleware travels to BiddingBuddyServices and shows
/// up in its logs under the same id. Registered via AddHttpMessageHandler on typed clients.
/// </summary>
public sealed class CorrelationHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cid = CorrelationContext.Current;
        if (!string.IsNullOrEmpty(cid) && !request.Headers.Contains("X-Correlation-Id"))
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", cid);

        return base.SendAsync(request, cancellationToken);
    }
}
