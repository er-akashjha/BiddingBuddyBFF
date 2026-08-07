using BiddingBuddy.Bff.Core.DTOs.Fit;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ITenderFitService
{
    /// <summary>
    /// The org's verdict on a tender, computing and persisting it if needed.
    /// </summary>
    /// <param name="forceRerun">Recompute even when a current report exists. Free when the stored
    /// report is stale (the buyer amended the notice — the customer must not pay for that).</param>
    Task<FitReportDto> GetOrComputeAsync(
        Guid orgId, Guid? userId, Guid tenderId, bool forceRerun = false, CancellationToken ct = default);

    /// <summary>The stored report without computing, metering, or touching anything. Null when the
    /// org has never run one. Used by surfaces that want to show a verdict they already own —
    /// the tender list, the bid pipeline — without ever charging for a page view.</summary>
    Task<FitReportDto?> GetExistingAsync(Guid orgId, Guid tenderId, CancellationToken ct = default);

    /// <summary>Store an LLM narrative against a report (the async worker's callback).</summary>
    Task SetNarrativeAsync(
        Guid orgId, Guid tenderId, string? narrative, string? model, string state, string? error,
        CancellationToken ct = default);

    /// <summary>
    /// The stored report as a .docx bid/no-bid note. Never computes and never meters — you can
    /// only export what you already ran, so this cannot become a second way to be charged.
    /// </summary>
    Task<FitDocumentResult> ExportAsync(Guid orgId, Guid tenderId, CancellationToken ct = default);

    /// <summary>
    /// Turn the report's blockers and gaps into checklist items on a bid, so the analysis
    /// becomes work the team can tick off rather than a page they read once and close.
    /// Idempotent on the item title, so re-running after a corrigendum tops up rather than
    /// duplicating.
    /// </summary>
    /// <returns>How many items were newly added.</returns>
    Task<int> PushToChecklistAsync(
        Guid orgId, Guid bidId, Guid tenderId, CancellationToken ct = default);
}

public sealed record FitDocumentResult(byte[] Content, string FileName)
{
    public const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
}
