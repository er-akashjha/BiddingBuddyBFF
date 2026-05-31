using System.Security.Claims;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
    IOAuthProviderService oauthProvider,
    IConfiguration config) : ControllerBase
{
    private static readonly HashSet<string> SupportedProviders = ["google", "github"];

    /// <summary>GET /api/auth/oauth/{provider}?returnUrl=/dashboard</summary>
    [HttpGet("oauth/{provider}")]
    public IActionResult InitiateOAuth(string provider, [FromQuery] string returnUrl = "/")
    {
        provider = provider.ToLower();
        if (!SupportedProviders.Contains(provider))
            return BadRequest(new { error = $"Provider '{provider}' is not supported." });

        var state = tokenService.GenerateStateToken(returnUrl);
        var authUrl = oauthProvider.GetAuthorizationUrl(provider, state);
        return Redirect(authUrl);
    }

    /// <summary>GET /api/auth/oauth/{provider}/callback</summary>
    [HttpGet("oauth/{provider}/callback")]
    public async Task<IActionResult> OAuthCallback(
        string provider,
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken ct)
    {
        provider = provider.ToLower();
        if (!SupportedProviders.Contains(provider))
            return BadRequest(new { error = "Unsupported provider." });

        if (!tokenService.TryValidateStateToken(state, out var returnUrl))
            return BadRequest(new { error = "Invalid or expired state token." });

        try
        {
            var tokens = await authService.HandleOAuthCallbackAsync(provider, code, ct);

            var frontendBase = config["Frontend:BaseUrl"] ?? "http://localhost:3000";
            var callbackPath = config["Frontend:AuthCallbackPath"] ?? "/auth/callback";
            var redirectUrl = $"{frontendBase}{callbackPath}" +
                              $"?access_token={Uri.EscapeDataString(tokens.AccessToken)}" +
                              $"&refresh_token={Uri.EscapeDataString(tokens.RefreshToken)}" +
                              $"&expires_in={tokens.ExpiresIn}" +
                              $"&return_url={Uri.EscapeDataString(returnUrl)}";

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            var frontendBase = config["Frontend:BaseUrl"] ?? "http://localhost:3000";
            return Redirect($"{frontendBase}/auth/error?message={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>POST /api/auth/refresh</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.RefreshAsync(request.RefreshToken, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    /// <summary>POST /api/auth/logout</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>GET /api/auth/me</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(user);
    }

    /// <summary>PATCH /api/auth/me</summary>
    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto, CancellationToken ct)
    {
        var user = await authService.UpdateProfileAsync(CurrentUserId, dto, ct);
        return Ok(user);
    }

    /// <summary>GET /api/auth/me/providers</summary>
    [HttpGet("me/providers")]
    [Authorize]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(new { providers = user.ConnectedProviders });
    }

    /// <summary>DELETE /api/auth/me/providers/{provider}</summary>
    [HttpDelete("me/providers/{provider}")]
    [Authorize]
    public async Task<IActionResult> UnlinkProvider(string provider, CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        if (user.ConnectedProviders.Count <= 1)
            return BadRequest(new { error = "Cannot unlink the only connected provider." });

        await authService.UnlinkProviderAsync(CurrentUserId, provider.ToLower(), ct);
        return NoContent();
    }

    private Guid CurrentUserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? throw new UnauthorizedAccessException("User ID not found in token.");
            return Guid.Parse(sub);
        }
    }
}
