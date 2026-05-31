namespace BiddingBuddy.Bff.Core.DTOs.Compliance;

public record ComplianceRequirementDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    bool IsMandatory,
    DateTime CreatedAt,
    IReadOnlyList<ComplianceDocumentDto> Documents
);

public record ComplianceDocumentDto(
    Guid Id,
    Guid RequirementId,
    Guid? DocumentId,
    string? DocumentName,
    string Status,
    DateOnly? ExpiryDate,
    Guid? VerifiedBy,
    DateTime? VerifiedAt,
    string? Notes,
    DateTime UpdatedAt
);

public record CreateRequirementDto(
    string Name,
    string? Description,
    string? Category,
    bool IsMandatory = true
);

public record UpdateRequirementDto(
    string? Name,
    string? Description,
    string? Category,
    bool? IsMandatory
);

public record AttachComplianceDocumentDto(
    Guid? DocumentId,
    string? Notes,
    DateOnly? ExpiryDate
);

public record UpdateComplianceDocumentDto(
    Guid? DocumentId,
    string? Status,
    string? Notes,
    DateOnly? ExpiryDate
);
