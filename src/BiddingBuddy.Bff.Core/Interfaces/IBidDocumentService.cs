using BiddingBuddy.Bff.Core.DTOs.Bids;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// A bid's document folder: org vault documents linked to the bid, unioned with the files
/// uploaded against its task-completion notes. Linking never copies bytes — the vault row and
/// its R2 object stay the single source of truth, so one certificate can serve many bids.
/// </summary>
public interface IBidDocumentService
{
    /// <summary>
    /// Everything in the bid's folder — linked vault documents plus task-completion
    /// attachments — newest first. Org-scoped; 404 if the bid isn't this org's.
    /// </summary>
    Task<IReadOnlyList<BidDocumentDto>> ListAsync(Guid orgId, Guid bidId, CancellationToken ct = default);

    /// <summary>
    /// Link an existing vault document to the bid. Idempotent: re-linking returns the
    /// existing row rather than erroring. 404 if either the bid or the document isn't
    /// this org's — which is also what stops a caller linking another org's document.
    /// </summary>
    Task<BidDocumentDto> LinkAsync(Guid orgId, Guid bidId, Guid userId, LinkBidDocumentDto dto, CancellationToken ct = default);

    /// <summary>
    /// Remove a vault document from the bid's folder. The vault document itself is untouched.
    /// 404 if the link doesn't exist. Idempotent from the caller's view only in that a
    /// second call 404s rather than corrupting anything.
    /// </summary>
    Task UnlinkAsync(Guid orgId, Guid bidId, Guid documentId, CancellationToken ct = default);
}
