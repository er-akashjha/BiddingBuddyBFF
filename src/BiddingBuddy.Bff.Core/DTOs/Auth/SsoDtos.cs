namespace BiddingBuddy.Bff.Core.DTOs.Auth;

/// <summary>
/// Answer to <c>GET /api/auth/sso/lookup?email=</c> — which sign-in button to press.
/// </summary>
/// <remarks>
/// Carries the provider and nothing else, deliberately. Returning the org name or id here would turn
/// an anonymous endpoint into a directory of who our customers are, keyed by any email domain a
/// caller cares to try. The SPA does not need it: all it does with this is choose between
/// "redirect to Microsoft" and "show the password field".
/// </remarks>
public record SsoLookupDto(string? Provider);

/// <summary>
/// The org's SSO configuration, for Settings → Single sign-on.
/// </summary>
/// <param name="CanBind">
/// Whether the CALLER can bind the tenant right now — true only when they have signed in with
/// Microsoft themselves. Drives whether the UI offers a "Connect" button or an explanation of why
/// it cannot, which is otherwise a confusing dead end.
/// </param>
/// <param name="CallerTenantId">
/// The tenant the caller would bind. Shown so an admin can confirm they are connecting the directory
/// they think they are before committing.
/// </param>
public record OrgSsoSettingsDto(
    Guid? EntraTenantId,
    string DefaultRole,
    IReadOnlyList<string> Domains,
    bool CanBind,
    Guid? CallerTenantId);

/// <summary>
/// Body for <c>POST /api/organizations/{id}/sso/entra</c>.
/// </summary>
/// <remarks>
/// There is no tenant id here, and that is the point. The tenant bound is always the one on the
/// caller's own Microsoft identity, so the only thing left to choose is what an auto-join grants.
/// Accepting a tenant id from the client would let anyone type a competitor's (publicly
/// discoverable) directory GUID and collect their staff on next sign-in.
/// </remarks>
public record BindEntraTenantDto(string? DefaultRole = null);
