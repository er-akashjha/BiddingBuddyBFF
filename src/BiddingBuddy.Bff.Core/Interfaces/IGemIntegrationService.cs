using BiddingBuddy.Bff.Core.DTOs.Integrations;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IGemIntegrationService
{
    Task<GemIntegrationDto?> GetAsync(Guid orgId, CancellationToken ct = default);
    Task<GemIntegrationDto> UpsertAsync(Guid orgId, UpsertGemIntegrationDto dto, CancellationToken ct = default);
}
