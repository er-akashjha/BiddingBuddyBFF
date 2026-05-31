using BiddingBuddy.Bff.Core.DTOs.Compliance;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IComplianceService
{
    Task<IReadOnlyList<ComplianceRequirementDto>> ListRequirementsAsync(Guid orgId, CancellationToken ct = default);
    Task<ComplianceRequirementDto> GetRequirementAsync(Guid requirementId, Guid orgId, CancellationToken ct = default);
    Task<ComplianceRequirementDto> CreateRequirementAsync(Guid orgId, CreateRequirementDto dto, CancellationToken ct = default);
    Task<ComplianceRequirementDto> UpdateRequirementAsync(Guid requirementId, Guid orgId, UpdateRequirementDto dto, CancellationToken ct = default);
    Task DeleteRequirementAsync(Guid requirementId, Guid orgId, CancellationToken ct = default);

    Task<ComplianceDocumentDto> AttachDocumentAsync(Guid requirementId, Guid orgId, Guid verifiedBy, AttachComplianceDocumentDto dto, CancellationToken ct = default);
    Task<ComplianceDocumentDto> UpdateComplianceDocumentAsync(Guid complianceDocId, Guid orgId, Guid verifiedBy, UpdateComplianceDocumentDto dto, CancellationToken ct = default);
    Task DetachDocumentAsync(Guid complianceDocId, Guid orgId, CancellationToken ct = default);
}
