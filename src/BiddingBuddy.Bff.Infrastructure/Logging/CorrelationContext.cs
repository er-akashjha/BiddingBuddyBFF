namespace BiddingBuddy.Bff.Infrastructure.Logging;

/// <summary>
/// Ambient async-local store for the current request's CorrelationId.
/// Set once by the API's correlation middleware; read by <see cref="CorrelationHeaderHandler"/>
/// to forward the id as <c>X-Correlation-Id</c> to downstream APIs (BiddingBuddyServices).
/// Mirrors BidProcessor's CorrelationContext so the same id threads UI → BFF → Services.
/// </summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    /// <summary>
    /// Set the ambient correlation id and return a disposable that restores the
    /// previous value when disposed — safe for nested scopes.
    /// </summary>
    public static IDisposable BeginScope(string correlationId)
    {
        var previous = _current.Value;
        _current.Value = correlationId;
        return new Scope(() => _current.Value = previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose();
        }
    }
}
