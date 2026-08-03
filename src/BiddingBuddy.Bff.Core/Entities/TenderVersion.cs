namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One immutable, hash-chained snapshot of a published tender. Appended on every publish,
/// corrigendum, award and cancellation; never updated, never deleted.
///
/// <para><b>The chain.</b> <c>ContentHash = sha256(canonical JSON of the snapshot)</c> and
/// <c>ChainHash = sha256(PrevChainHash + ContentHash)</c>, with version 1 taking an empty
/// <c>PrevChainHash</c> as genesis. Altering any historical row changes its content hash, which
/// changes every chain hash after it — so tampering is detectable by replaying the chain, which is
/// exactly what the downloadable audit file lets an inspector do without trusting us.</para>
///
/// <para><b>This is tamper-evident, not tamper-proof.</b> Whoever can write these rows can also
/// rebuild the chain from scratch. Closing that needs an external anchor — RFC 3161 timestamping
/// from a licensed CA, which the STQC guidelines require and which is Phase 3 work. Claiming more
/// than evidence here would be the kind of overstatement an audit is designed to catch.</para>
///
/// Maps to <c>tender_versions</c> (migration 0031).
/// </summary>
public class TenderVersion
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }

    /// <summary>1-based, gapless, unique per draft.</summary>
    public int Version { get; set; }

    /// <summary>published | corrigendum | award | cancellation.</summary>
    public string Reason { get; set; } = "published";

    /// <summary>The complete canonical snapshot as JSON. Self-contained on purpose: the audit file
    /// must stay readable after the draft it came from is gone.</summary>
    public string Snapshot { get; set; } = "{}";

    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Empty string on version 1 (genesis), else the previous row's <see cref="ChainHash"/>.</summary>
    public string PrevChainHash { get; set; } = string.Empty;

    public string ChainHash { get; set; } = string.Empty;

    /// <summary>The compliance rule set in force when this version was published — not the one in
    /// force now. Pinned so the version can be re-evaluated under the rules it was published under.</summary>
    public string RuleSetVersion { get; set; } = string.Empty;

    public Guid PublishedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public TenderDraft Draft { get; set; } = default!;
}
