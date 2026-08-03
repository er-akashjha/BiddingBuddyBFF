using BiddingBuddy.Bff.Core.DTOs.Auth;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Enterprise single sign-on: binding an organization to its Microsoft Entra directory, and the
/// anonymous domain lookup that routes a login box to the right provider.
/// </summary>
/// <remarks>
/// Split out of <c>IOrganizationService</c> rather than bolted onto it because the two halves have
/// opposite trust models — one is anonymous and answers with as little as possible, the other is the
/// most privileged write in the org surface — and keeping them in one file invites reusing the wrong
/// helper across that line.
/// </remarks>
public interface ISsoService
{
    /// <summary>
    /// Anonymous. Which provider, if any, owns this email's domain. Returns a null provider for an
    /// unknown domain, a malformed address, or an inactive org — never throws, never distinguishes.
    /// </summary>
    Task<SsoLookupDto> LookupByEmailAsync(string? email, CancellationToken ct = default);

    Task<OrgSsoSettingsDto> GetSettingsAsync(Guid orgId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Binds the org to the tenant on the CALLER's own Microsoft identity. The caller must be an
    /// owner or admin AND have signed in with Microsoft.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Caller is not an owner/admin.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>NO_MICROSOFT_IDENTITY</c> — the caller has no Microsoft account linked, so there is no
    /// tenant they have proven they belong to. <c>TENANT_ALREADY_BOUND</c> — another workspace owns
    /// that directory.
    /// </exception>
    Task<OrgSsoSettingsDto> BindEntraTenantAsync(
        Guid orgId, Guid userId, BindEntraTenantDto dto, CancellationToken ct = default);

    /// <summary>
    /// Disconnects the directory. Existing members keep their access — this stops future auto-joins,
    /// it is not a way to mass-remove people.
    /// </summary>
    Task<OrgSsoSettingsDto> UnbindEntraTenantAsync(Guid orgId, Guid userId, CancellationToken ct = default);
}
