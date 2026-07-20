namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    string? Phone,
    IReadOnlyList<UserOrgDto> Organizations,
    IReadOnlyList<string> ConnectedProviders
);

/// <param name="GemSellerName">The org's seller identity on GeM award ladders. Carried on /me (like
/// PrimaryCategory) because the SPA needs it on every award surface — to highlight "our" row on a
/// price ladder and to compute our rank — and re-fetching the org on each of those would be silly.</param>
public record UserOrgDto(
    Guid Id,
    string Name,
    string? Slug,
    string Role,
    string? LogoUrl,
    bool IsActive,
    string? PrimaryCategory,
    string? GemSellerName = null
);
