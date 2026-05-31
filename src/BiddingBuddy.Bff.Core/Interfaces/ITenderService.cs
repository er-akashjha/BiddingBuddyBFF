using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ITenderService
{
    Task<PagedResult<TenderListItemDto>> ListAsync(Guid orgId, TenderFiltersDto filters, CancellationToken ct = default);
    Task<TenderDetailDto> GetAsync(Guid tenderId, Guid orgId, CancellationToken ct = default);
    Task<OrgTenderSettingsDto> SaveAsync(Guid tenderId, Guid orgId, Guid userId, SaveTenderDto dto, CancellationToken ct = default);
    Task UnsaveAsync(Guid tenderId, Guid orgId, CancellationToken ct = default);
    Task<OrgTenderSettingsDto> UpdateSettingsAsync(Guid tenderId, Guid orgId, SaveTenderDto dto, CancellationToken ct = default);
}
