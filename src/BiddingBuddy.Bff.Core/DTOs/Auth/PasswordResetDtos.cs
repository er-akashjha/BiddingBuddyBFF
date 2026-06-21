namespace BiddingBuddy.Bff.Core.DTOs.Auth;

/// <summary>Body for <c>POST /api/auth/forgot-password</c>: request a reset code for an email.</summary>
public record ForgotPasswordDto(
    string Email
);

/// <summary>
/// Returned from <c>POST /api/auth/forgot-password</c>. Always the same shape regardless
/// of whether the email maps to a resettable account (no enumeration). <c>DevCode</c> is
/// populated ONLY in the Development environment to allow end-to-end testing without a mailbox.
/// </summary>
public record PasswordResetRequestedDto(
    string Status,
    int ExpiresInSeconds,
    string? DevCode = null
);

/// <summary>Body for <c>POST /api/auth/reset-password</c>: the email + 6-digit code + new password.</summary>
public record ResetPasswordDto(
    string Email,
    string Code,
    string NewPassword
);
