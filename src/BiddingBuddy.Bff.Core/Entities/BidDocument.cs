namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Links an org vault <see cref="Document"/> to a <see cref="Bid"/> — the rows behind a bid's
/// document folder. A link, not a copy: the same GST certificate can be attached to any number
/// of bids while living once in the vault, so re-uploading it updates every bid that uses it.
/// <para>
/// Distinct from <see cref="BidAttachment"/>, which owns its own R2 object and exists only for
/// the file attached to a task-completion note. The bid's document list is the union of both.
/// </para>
/// </summary>
public class BidDocument
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid BidId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid LinkedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Bid Bid { get; set; } = default!;
    public Document Document { get; set; } = default!;
    public User Linker { get; set; } = default!;
}
