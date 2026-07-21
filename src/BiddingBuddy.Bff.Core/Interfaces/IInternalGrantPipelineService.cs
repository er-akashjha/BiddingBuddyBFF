using BiddingBuddy.Bff.Core.DTOs.Grants;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>Pipeline-facing grant ingest. Called by BidProcessor over <c>X-Api-Key</c>.</summary>
public interface IInternalGrantPipelineService
{
    /// <summary>
    /// Upserts a grant into the Postgres shadow index, keyed on
    /// <c>(platform, platformGrantId)</c>. Returns the row id and whether it was created.
    /// </summary>
    Task<UpsertGrantResponseDto> UpsertGrantAsync(UpsertGrantDto dto, CancellationToken ct);
}
