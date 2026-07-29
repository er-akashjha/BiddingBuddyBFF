namespace BiddingBuddy.Bff.Core.DTOs.Auth;

public record UserDto(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    string? Phone,
    IReadOnlyList<UserOrgDto> Organizations,
    IReadOnlyList<string> ConnectedProviders,

    /// <summary>
    /// True for a platform operator (one of ours). Drives whether the SPA renders the
    /// operator-only admin surface (the buyer-access review queue). Orthogonal to any org role,
    /// so it rides on the user, not on <see cref="UserOrgDto"/>. Appended and defaulted so older
    /// callers and any other construction keep compiling. The server is the real gate — every
    /// <c>/api/admin/*</c> route re-checks this flag regardless of what the client renders.
    /// </summary>
    bool IsPlatformAdmin = false
);

/// <param name="GemSellerName">The org's seller identity on GeM award ladders. Carried on /me (like
/// PrimaryCategory) because the SPA needs it on every award surface — to highlight "our" row on a
/// price ladder and to compute our rank — and re-fetching the org on each of those would be silly.</param>
public record UserOrgDto(
    Guid Id,
    string Name,
    string? Slug,
    string Role,
    string? LogoUrl,
    bool IsActive,
    string? PrimaryCategory,
    string? GemSellerName = null,

    /// <summary>
    /// supplier | buyer | both. Decides which half of the product this org sees — the client hides
    /// the whole tender-authoring surface for a supplier-only org.
    ///
    /// <para>Defaults to <c>supplier</c> so an older client, or an org row predating migration
    /// 0031, degrades to today's behaviour rather than exposing a department's tooling to every
    /// supplier.</para>
    /// </summary>
    string OrgType = "supplier"
);
