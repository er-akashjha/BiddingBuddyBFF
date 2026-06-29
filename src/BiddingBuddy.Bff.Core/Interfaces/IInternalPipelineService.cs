using BiddingBuddy.Bff.Core.DTOs.Internal;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IInternalPipelineService
{
    Task<UpsertTenderResponseDto> UpsertTenderAsync(UpsertTenderDto dto, CancellationToken ct = default);

    /// <summary>
    /// One-time backfill: fills <c>mongo_tender_id</c> on existing tenders that predate
    /// migration 0010 (NULL until the pipeline next re-enriches them). For each NULL row it
    /// looks up the canonical Mongo tender by gem id via BiddingBuddyServices and stores its
    /// <c>_id</c>. Processes one bounded batch; call repeatedly until <c>Remaining</c> is 0.
    /// </summary>
    Task<BackfillTenderMongoIdResultDto> BackfillTenderMongoIdsAsync(int batchSize, CancellationToken ct = default);
    Task UpsertDocumentContentAsync(string gemTenderId, UpsertDocumentContentDto dto, CancellationToken ct = default);
    Task UpsertCompetitorAsync(UpsertCompetitorDto dto, CancellationToken ct = default);
    Task UpsertAiAnalysisAsync(UpsertAiAnalysisDto dto, CancellationToken ct = default);
}
