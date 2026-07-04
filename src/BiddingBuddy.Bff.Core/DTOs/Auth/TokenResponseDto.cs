namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record TokenResponseDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,          // seconds
    bool IsNewUser = false  // true only on the OAuth-callback path when the account was created by this call
);
