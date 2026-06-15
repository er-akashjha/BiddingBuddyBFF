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

/// <summary>One pending invite (no <c>users</c> row yet) as the SPA's admin Teams page sees it.</summary>
public record PendingInviteDto(
    Guid Id,
    string Email,
    string Role,
    string? Department,
    Guid InvitedBy,
    string? InvitedByName,
    DateTime ExpiresAt,
    DateTime CreatedAt
);

/// <summary>Discriminated response for <c>POST /api/organizations/{id}/members</c>.</summary>
public record InviteMemberResultDto(
    /// <summary><c>"added"</c> when the invitee already had a user account and is now an active member;
    /// <c>"invited"</c> when the invitee did not exist and a pending invite was emailed.</summary>
    string Status,

    /// <summary>Populated when <see cref="Status"/> is <c>"added"</c>.</summary>
    OrgMemberDto? Member,

    /// <summary>Populated when <see cref="Status"/> is <c>"invited"</c> — the email the invitation was sent to.</summary>
    string? InvitedEmail,

    /// <summary>Populated when <see cref="Status"/> is <c>"invited"</c> — when the invite token stops working.</summary>
    DateTime? ExpiresAt
);

public record UpdateMemberDto(string? Role, string? Department, string? Status);
