namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record RegisterDto(
    string Name,
    string Email,
    string Password,
    string OrgName,
    string? Phone
);

public record LoginWithPasswordDto(
    string Email,
    string Password
);
