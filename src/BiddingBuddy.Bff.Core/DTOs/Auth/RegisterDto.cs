namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record RegisterDto(
    string Name,
    string Email,
    string Password,
    string OrgName,
    string? Phone,
    // Optional single-use invite token. When supplied, the new user joins the
    // inviting organization (with the role recorded on the invite) INSTEAD of
    // creating a personal org from OrgName. OrgName is then ignored.
    string? InviteToken = null
);

public record LoginWithPasswordDto(
    string Email,
    string Password
);
