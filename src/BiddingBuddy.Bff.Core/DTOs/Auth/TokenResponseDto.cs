namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record TokenResponseDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn       // seconds
);
