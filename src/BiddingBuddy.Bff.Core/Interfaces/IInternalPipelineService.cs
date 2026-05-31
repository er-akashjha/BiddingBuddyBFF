using BiddingBuddy.Bff.Core.DTOs.Internal;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IInternalPipelineService
{
    Task<UpsertTenderResponseDto> UpsertTenderAsync(UpsertTenderDto dto, CancellationToken ct = default);
    Task UpsertDocumentContentAsync(string gemTenderId, UpsertDocumentContentDto dto, CancellationToken ct = default);
    Task UpsertCompetitorAsync(UpsertCompetitorDto dto, CancellationToken ct = default);
    Task UpsertAiAnalysisAsync(UpsertAiAnalysisDto dto, CancellationToken ct = default);
}
