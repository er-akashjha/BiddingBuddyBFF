namespace BiddingBuddy.Bff.Core.DTOs.Auth;

/// <summary>
/// Returned from <c>POST /api/auth/register</c> now that signup no longer creates
/// an account directly. The caller must complete <c>POST /api/auth/verify-email</c>
/// with the emailed OTP before any user/org is created.
/// </summary>
/// <param name="Status">Always <c>"verification_pending"</c>.</param>
/// <param name="Email">The (normalised) email the code was sent to.</param>
/// <param name="ExpiresInSeconds">OTP lifetime, so the client can show a countdown.</param>
/// <param name="DevCode">The raw OTP — populated ONLY in the Development environment
/// to allow end-to-end testing without a working mail pipeline. Always null in prod.</param>
public record RegistrationPendingDto(
    string Status,
    string Email,
    int ExpiresInSeconds,
    string? DevCode = null
);

/// <summary>Body for <c>POST /api/auth/verify-email</c>: the pending email + the 6-digit code.</summary>
public record VerifyEmailDto(
    string Email,
    string Code
);

/// <summary>Body for <c>POST /api/auth/resend-verification</c>: re-issue an OTP for a pending signup.</summary>
public record ResendVerificationDto(
    string Email
);
