using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Buyer-side tendering: a government department authors a tender notice and publishes it.
///
/// <para>Phase 1 (e-publishing) only. We host and distribute the NOTICE; bids are still received
/// wherever the department receives them today. Because no bid ever touches this system, none of the
/// STQC certification, PKI or HSM machinery that governs e-procurement applies — that is Phase 3,
/// and the boundary is deliberate rather than incidental.</para>
///
/// <para>Every mutating method records an audit event, and every publication appends a hash-chained
/// immutable version. Nothing here hard-deletes anything.</para>
/// </summary>
public interface IBuyerTenderService
{
    /// <summary>Canonical dropdown values for the authoring form, served from the same source the
    /// validator enforces so the form cannot offer a value publish would reject.</summary>
    Task<BuyerTenderOptionsDto> GetOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TenderDraftListItemDto>> ListAsync(
        Guid orgId, string? status, string? search, CancellationToken ct = default);

    /// <summary>Null when the draft does not exist or belongs to another organization — the two are
    /// deliberately indistinguishable to the caller, so this endpoint cannot be used to discover
    /// that a given draft id exists elsewhere.</summary>
    Task<TenderDraftDetailDto?> GetAsync(Guid orgId, Guid draftId, CancellationToken ct = default);

    Task<TenderDraftDetailDto> CreateAsync(
        Guid orgId, Guid userId, string actorRole, SaveTenderDraftDto dto, CancellationToken ct = default);

    /// <summary>
    /// Applies only the non-null fields of <paramref name="dto"/>, so the multi-step form can save
    /// one section without carrying the others.
    /// </summary>
    Task<TenderDraftDetailDto?> UpdateAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, SaveTenderDraftDto dto, CancellationToken ct = default);

    /// <summary>Runs the compliance engine without changing anything. Safe to call on every keystroke.</summary>
    Task<ValidationResultDto?> ValidateAsync(Guid orgId, Guid draftId, CancellationToken ct = default);

    /// <summary>
    /// Publishes: validates, snapshots, appends a hash-chained version, projects into the Mongo
    /// corpus as <c>platform="direct"</c>, and notifies the publisher.
    /// </summary>
    /// <exception cref="Exceptions.ValidationFailedException">
    /// Compliance errors exist, or warnings exist and were not acknowledged.
    /// </exception>
    Task<TenderDraftDetailDto?> PublishAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, PublishTenderDto dto, CancellationToken ct = default);

    /// <summary>
    /// Applies an amendment to a published tender: appends a corrigendum with a field-level diff,
    /// appends a version, re-projects, and notifies every supplier the matching rail told about this
    /// tender.
    /// </summary>
    Task<TenderDraftDetailDto?> IssueCorrigendumAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, IssueCorrigendumDto dto, CancellationToken ct = default);

    /// <summary>Records the award. Rule 159 requires contract award information to be published.</summary>
    Task<TenderDraftDetailDto?> AwardAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, AwardTenderDto dto, CancellationToken ct = default);

    /// <summary>Cancels a published tender. Recorded as a corrigendum, because bidders must be told.</summary>
    Task<TenderDraftDetailDto?> CancelAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, string reason, CancellationToken ct = default);

    /// <summary>Deletes an UNPUBLISHED draft. A published tender can only be cancelled — there is no
    /// path in this service that removes a published notice or its version history.</summary>
    Task<bool> DeleteDraftAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId, CancellationToken ct = default);

    Task<IReadOnlyList<CommitteeMemberDto>> SetCommitteeAsync(
        Guid orgId, Guid userId, string actorRole, Guid draftId,
        IReadOnlyList<SaveCommitteeMemberDto> members, CancellationToken ct = default);

    /// <summary>
    /// The downloadable audit file: the tender, every version, every corrigendum, every recorded
    /// action, and a replay of the hash chain so an inspector can verify it without trusting us.
    /// </summary>
    Task<TenderAuditFileDto?> GetAuditFileAsync(Guid orgId, Guid draftId, CancellationToken ct = default);
}
