using System.Security.Claims;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    IAuthService authService,
    ITokenService tokenService,
    IOAuthProviderService oauthProvider,
    IConfiguration config) : ControllerBase
{
    private static readonly HashSet<string> SupportedProviders = ["google", "github"];

    /// <summary>
    /// Begin a password signup. Sends a 6-digit verification code to the email and
    /// returns 202 — the account is created only after the code is confirmed via
    /// <c>POST /api/auth/verify-email</c>.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(RegistrationPendingDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
    {
        try
        {
            var result = await authService.StartRegistrationAsync(dto, ct);
            return StatusCode(StatusCodes.Status202Accepted, result);
        }
        catch (ArgumentException ex)
        {
            // Covers password-too-short and INVITE_INVALID.
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_EXISTS")
        {
            return Conflict(new { error = "Email already registered." });
        }
    }

    /// <summary>Confirm a signup's 6-digit code → creates the account and returns tokens.</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.VerifyEmailAsync(dto, ct);
            return Ok(tokens);
        }
        catch (InvalidOperationException ex) when (ex.Message == "TOO_MANY_ATTEMPTS")
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_EXISTS")
        {
            return Conflict(new { error = "Email already registered." });
        }
        catch (InvalidOperationException ex)   // CODE_INVALID (wrong/expired/unknown)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ArgumentException ex)            // INVITE_INVALID re-checked at consume time
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Re-send the verification code for a pending signup. Always 204 (no enumeration).</summary>
    [HttpPost("resend-verification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto dto, CancellationToken ct)
    {
        await authService.ResendVerificationAsync(dto.Email, ct);
        return NoContent();
    }

    /// <summary>Login with email and password. Returns access + refresh tokens.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginWithPasswordDto dto, CancellationToken ct)
    {
        try
        {
            var tokens = await authService.LoginWithPasswordAsync(dto, ct);
            return Ok(tokens);
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }
    }

    /// <summary>Redirect the browser to the OAuth provider consent page (Google or GitHub).</summary>
    [HttpGet("oauth/{provider}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult InitiateOAuth(string provider, [FromQuery] string returnUrl = "/")
    {
        provider = provider.ToLower();
        if (!SupportedProviders.Contains(provider))
            return BadRequest(new { error = $"Provider '{provider}' is not supported." });

        var state = tokenService.GenerateStateToken(returnUrl);
        var authUrl = oauthProvider.GetAuthorizationUrl(provider, state);
        return Redirect(authUrl);
    }

    /// <summary>OAuth callback — exchanges code for tokens and redirects to the frontend.</summary>
    [HttpGet("oauth/{provider}/callback")]
    [ProducesResponseType(StatusCodes.Status302Found)]
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

    /// <summary>Rotate refresh token — returns a new access token + refresh token pair.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
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

    /// <summary>Revoke the current refresh token (logout).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Get the current user's profile, organizations and connected OAuth providers.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(user);
    }

    /// <summary>Update the current user's name or phone number.</summary>
    [HttpPatch("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto, CancellationToken ct)
    {
        var user = await authService.UpdateProfileAsync(CurrentUserId, dto, ct);
        return Ok(user);
    }

    /// <summary>List OAuth providers (google, github) linked to the current user.</summary>
    [HttpGet("me/providers")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var user = await authService.GetCurrentUserAsync(CurrentUserId, ct);
        return Ok(new { providers = user.ConnectedProviders });
    }

    /// <summary>Unlink an OAuth provider from the current user (must have at least one provider remaining).</summary>
    [HttpDelete("me/providers/{provider}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
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
