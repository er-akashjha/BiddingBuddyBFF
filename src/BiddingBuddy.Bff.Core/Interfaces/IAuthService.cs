using BiddingBuddy.Bff.Core.DTOs.Auth;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Begin a password signup: validate, stash hashed credentials in
    /// pending_registrations, and email a 6-digit OTP. Does NOT create the account —
    /// call <see cref="VerifyEmailAsync"/> with the code to finish.
    /// </summary>
    Task<RegistrationPendingDto> StartRegistrationAsync(RegisterDto dto, CancellationToken ct = default);
    /// <summary>Confirm the OTP for a pending signup; creates the user/org and returns tokens.</summary>
    Task<TokenResponseDto> VerifyEmailAsync(VerifyEmailDto dto, CancellationToken ct = default);
    /// <summary>Re-issue an OTP for a pending signup. No-op (silent) if none is active.</summary>
    Task ResendVerificationAsync(string email, CancellationToken ct = default);
    Task<TokenResponseDto> LoginWithPasswordAsync(LoginWithPasswordDto dto, CancellationToken ct = default);
    Task<TokenResponseDto> HandleOAuthCallbackAsync(string provider, string code, CancellationToken ct = default);
    Task<TokenResponseDto> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct = default);
    Task UnlinkProviderAsync(Guid userId, string provider, CancellationToken ct = default);
}
