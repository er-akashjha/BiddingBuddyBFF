namespace BiddingBuddy.Bff.Core.DTOs.Orgs;

public record CreateOrgDto(
    string Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? PrimaryCategory
);

public record UpdateOrgDto(
    string? Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? PrimaryCategory,
    string? LogoUrl
);

public record OrgDetailDto(
    Guid Id,
    string Name,
    string? Slug,
    string? Gstin,
    string? Pan,
    string? Industry,
    string? CompanySize,
    string? RegisteredAddress,
    string? City,
    string? State,
    string? Pincode,
    string? Website,
    string? GemSellerId,
    string? PrimaryCategory,
    string? LogoUrl,
    bool IsActive,
    string UserRole,
    DateTime CreatedAt,
    IReadOnlyList<OrgMemberDto> Members
);

public record OrgMemberDto(
    Guid Id,
    Guid UserId,
    string Name,
    string Email,
    string? AvatarUrl,
    string Role,
    string? Department,
    string Status,
    DateTime? JoinedAt
);

public record InviteMemberDto(string Email, string Role, string? Department);

public record UpdateMemberDto(string? Role, string? Department, string? Status);
