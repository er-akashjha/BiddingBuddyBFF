using BiddingBuddy.Bff.Core.DTOs.Auth;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IAuthService
{
    Task<TokenResponseDto> HandleOAuthCallbackAsync(string provider, string code, CancellationToken ct = default);
    Task<TokenResponseDto> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task<UserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto, CancellationToken ct = default);
    Task UnlinkProviderAsync(Guid userId, string provider, CancellationToken ct = default);
}
