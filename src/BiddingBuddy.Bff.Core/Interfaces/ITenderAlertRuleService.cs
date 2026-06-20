using BiddingBuddy.Bff.Core.DTOs.Alerts;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ITenderAlertRuleService
{
    Task<IReadOnlyList<TenderAlertRuleDto>> ListAsync(Guid orgId, CancellationToken ct = default);
    Task<TenderAlertRuleDto> CreateAsync(Guid orgId, Guid userId, CreateTenderAlertRuleDto dto, CancellationToken ct = default);
    Task<TenderAlertRuleDto> UpdateAsync(Guid ruleId, Guid orgId, UpdateTenderAlertRuleDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid ruleId, Guid orgId, CancellationToken ct = default);

    Task<OrgAlertSettingsDto> GetSettingsAsync(Guid orgId, CancellationToken ct = default);
    Task<OrgAlertSettingsDto> UpdateSettingsAsync(Guid orgId, UpdateOrgAlertSettingsDto dto, CancellationToken ct = default);
}
