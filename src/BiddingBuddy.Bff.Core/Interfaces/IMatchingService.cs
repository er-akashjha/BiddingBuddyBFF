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

    /// <summary>
    /// Scheduled scan: evaluates every tender not yet marked <c>alerts_scanned_at</c>
    /// against active interest rules, records matches, emails one digest per matched
    /// org, then stamps the tenders scanned. Idempotent across runs (the flag + the
    /// <c>tender_matches</c> UNIQUE(org,tender) constraint prevent re-sends). This is
    /// the primary delivery path; <see cref="OnTenderUpsertedAsync"/> is retained for
    /// optional real-time matching.
    /// </summary>
    /// <param name="batchSize">Tenders evaluated per DB round-trip within one run.</param>
    /// <param name="rearmFirst">
    /// When true, clears <c>alerts_scanned_at</c> on ALL tenders before scanning — an
    /// explicit backfill that re-evaluates the whole backlog. Use sparingly.
    /// </param>
    Task<TenderScanResult> ScanNewTendersAsync(int batchSize, bool rearmFirst = false, CancellationToken ct = default);
}

/// <summary>Outcome of one <see cref="IMatchingService.ScanNewTendersAsync"/> run.</summary>
/// <param name="TendersScanned">Tenders evaluated and stamped this run.</param>
/// <param name="MatchesCreated">New <c>tender_matches</c> rows inserted.</param>
/// <param name="OrgsNotified">Org digests dispatched (orgs with ≥1 recipient).</param>
/// <param name="Skipped">True if another scan was already running and this call no-opped.</param>
public record TenderScanResult(int TendersScanned, int MatchesCreated, int OrgsNotified, bool Skipped);
