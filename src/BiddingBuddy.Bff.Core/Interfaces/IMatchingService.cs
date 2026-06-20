namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Matches freshly-upserted tenders against client interest rules and delivers
/// them as batched digest notifications.
/// </summary>
public interface IMatchingService
{
    /// <summary>
    /// Called after a tender is upserted (from the pipeline). Tests the tender
    /// against every active rule, buffers a <c>tender_matches</c> row per matched
    /// org (deduped), then flushes any org whose pending buffer has reached its
    /// digest size. Best-effort: never throws into the caller's critical path.
    /// </summary>
    Task OnTenderUpsertedAsync(Guid tenderId, CancellationToken ct = default);

    /// <summary>
    /// Time-fallback flush: delivers whatever is buffered for every org with
    /// pending matches, regardless of digest size, and expires matches whose
    /// tender deadline has passed. Returns the number of orgs that received a
    /// digest. Intended to be driven on a schedule (e.g. daily).
    /// </summary>
    Task<int> FlushAllDueAsync(CancellationToken ct = default);
}
