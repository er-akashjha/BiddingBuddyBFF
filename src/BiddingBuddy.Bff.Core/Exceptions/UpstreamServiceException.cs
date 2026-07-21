namespace BiddingBuddy.Bff.Core.Exceptions;

/// <summary>
/// A downstream service the BFF depends on (BiddingBuddyServices, etc.) failed or was
/// unreachable. Distinct from <see cref="InvalidOperationException"/>, which
/// <c>GlobalExceptionHandler</c> maps to 400 — an upstream fault is not the caller's
/// fault, and reporting it as 400 sent both clients and log readers down the wrong path.
/// Maps to 502 Bad Gateway.
/// </summary>
public sealed class UpstreamServiceException : Exception
{
    /// <summary>Name of the upstream service, for logging and the problem detail.</summary>
    public string Service { get; }

    /// <summary>Status the upstream returned, or null when it was never reached.</summary>
    public int? UpstreamStatus { get; }

    public UpstreamServiceException(
        string service, string message, int? upstreamStatus = null, Exception? inner = null)
        : base(message, inner)
    {
        Service        = service;
        UpstreamStatus = upstreamStatus;
    }
}
