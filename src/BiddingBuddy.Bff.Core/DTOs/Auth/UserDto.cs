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

public record UserOrgDto(
    Guid Id,
    string Name,
    string? Slug,
    string Role,
    string? LogoUrl,
    bool IsActive,
    string? PrimaryCategory
);
