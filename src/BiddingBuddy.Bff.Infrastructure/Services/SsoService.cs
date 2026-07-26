using BiddingBuddy.Bff.Core.Authorization;
using BiddingBuddy.Bff.Core.DTOs.Auth;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class SsoService(BffDbContext db, ILogger<SsoService> log) : ISsoService
{
    private const string MicrosoftProvider = "microsoft";

    public async Task<SsoLookupDto> LookupByEmailAsync(string? email, CancellationToken ct = default)
    {
        var domain = ExtractDomain(email);
        if (domain is null) return new SsoLookupDto(null);

        // Join through to the org so a deactivated workspace stops routing. Otherwise disabling an
        // account would leave its former staff bouncing to an IdP that can no longer place them
        // anywhere — a login page that cannot be used and cannot be escaped.
        var routed = await db.OrgSsoDomains
            .Where(d => d.Domain == domain)
            .Join(db.Organizations.Where(o => o.IsActive && o.EntraTenantId != null),
                  d => d.OrgId, o => o.Id, (d, o) => o.Id)
            .AnyAsync(ct);

        return new SsoLookupDto(routed ? MicrosoftProvider : null);
    }

    public async Task<OrgSsoSettingsDto> GetSettingsAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        return await BuildSettingsAsync(org, userId, ct);
    }

    public async Task<OrgSsoSettingsDto> BindEntraTenantAsync(
        Guid orgId, Guid userId, BindEntraTenantDto dto, CancellationToken ct = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        await RequireOwnerOrAdminAsync(orgId, userId, ct);

        // THE check. The tenant bound is the one the caller personally authenticated against —
        // never a value they supplied. An admin can only hand us a directory they have already
        // proven, to Microsoft, that they belong to; there is no request shape that binds someone
        // else's. This is what stands between the feature and "type a competitor's tenant GUID,
        // collect their employees".
        var callerTenant = await CallerTenantIdAsync(userId, ct)
            ?? throw new InvalidOperationException("NO_MICROSOFT_IDENTITY");

        var takenByAnother = await db.Organizations
            .AnyAsync(o => o.EntraTenantId == callerTenant && o.Id != orgId, ct);
        if (takenByAnother)
            throw new InvalidOperationException("TENANT_ALREADY_BOUND");

        org.EntraTenantId = callerTenant;

        // Fail closed on an unknown role rather than writing one the CHECK constraint would reject
        // at SaveChanges — the constraint would be a 500, this is a deliberate downgrade.
        if (dto.DefaultRole is { Length: > 0 } requested)
        {
            org.SsoDefaultRole = OrgRoles.All.Contains(requested) ? requested : OrgRoles.Viewer;
            if (org.SsoDefaultRole != requested)
                log.LogWarning("Rejected unknown sso_default_role '{Role}' for org {OrgId}; using viewer.",
                    requested, orgId);
        }

        org.UpdatedAt = DateTime.UtcNow;

        // Seed the routing domain from the binding admin's own address. Their account is in the
        // tenant being bound, so their domain is one Microsoft already made that tenant prove by
        // DNS — which is why routing needs no verification flow of ours. Later sign-ins from other
        // domains in the same directory add themselves via AuthService.
        var adminEmail = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
        await AddDomainIfFreeAsync(orgId, ExtractDomain(adminEmail), ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index caught a bind that raced another workspace.
            db.ChangeTracker.Clear();
            log.LogWarning(ex, "Entra tenant bind for org {OrgId} lost a race on the unique index.", orgId);
            throw new InvalidOperationException("TENANT_ALREADY_BOUND");
        }

        log.LogInformation("Org {OrgId} bound to Entra tenant {TenantId} by user {UserId}.",
            orgId, callerTenant, userId);

        return await BuildSettingsAsync(org, userId, ct);
    }

    public async Task<OrgSsoSettingsDto> UnbindEntraTenantAsync(
        Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        await RequireOwnerOrAdminAsync(orgId, userId, ct);

        org.EntraTenantId = null;
        org.UpdatedAt = DateTime.UtcNow;

        // Drop the routing too, or the login page keeps sending this domain to Microsoft for a
        // workspace that will no longer take anyone — a redirect loop with no way back to the
        // password box. Memberships are untouched: disconnecting SSO stops future auto-joins, it
        // is not a way to remove the people already here.
        //
        // RemoveRange rather than ExecuteDelete: the set is a handful of rows per org, it joins the
        // same SaveChanges as the tenant clear instead of being a separate statement that could
        // land without it, and ExecuteDelete cannot be translated by the in-memory provider the
        // tests run on — an untestable unbind is not worth the one saved round-trip.
        var domains = await db.OrgSsoDomains.Where(d => d.OrgId == orgId).ToListAsync(ct);
        db.OrgSsoDomains.RemoveRange(domains);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Org {OrgId} disconnected from its Entra tenant by user {UserId}.", orgId, userId);

        return await BuildSettingsAsync(org, userId, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<OrgSsoSettingsDto> BuildSettingsAsync(Organization org, Guid userId, CancellationToken ct)
    {
        var domains = await db.OrgSsoDomains
            .Where(d => d.OrgId == org.Id)
            .OrderBy(d => d.Domain)
            .Select(d => d.Domain)
            .ToListAsync(ct);

        var callerTenant = await CallerTenantIdAsync(userId, ct);

        return new OrgSsoSettingsDto(
            EntraTenantId:  org.EntraTenantId,
            DefaultRole:    org.SsoDefaultRole,
            Domains:        domains,
            CanBind:        callerTenant is not null,
            CallerTenantId: callerTenant);
    }

    /// <summary>The Entra tenant the caller has actually authenticated against, if any.</summary>
    private Task<Guid?> CallerTenantIdAsync(Guid userId, CancellationToken ct)
        => db.OAuthAccounts
            .Where(a => a.UserId == userId && a.Provider == MicrosoftProvider && a.TenantId != null)
            .Select(a => a.TenantId)
            .FirstOrDefaultAsync(ct);

    private async Task RequireOwnerOrAdminAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        var role = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active")
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);

        if (role is not (OrgRoles.Owner or OrgRoles.Admin))
            throw new UnauthorizedAccessException("Only an owner or admin can change single sign-on.");
    }

    private async Task AddDomainIfFreeAsync(Guid orgId, string? domain, CancellationToken ct)
    {
        if (domain is null || ConsumerEmailDomains.Contains(domain)) return;
        if (await db.OrgSsoDomains.AnyAsync(d => d.Domain == domain, ct)) return;

        db.OrgSsoDomains.Add(new OrgSsoDomain
        {
            Id        = Guid.NewGuid(),
            OrgId     = orgId,
            Domain    = domain,
            Source    = "entra",
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Bare lower-cased domain, or null if the input is not one address.</summary>
    internal static string? ExtractDomain(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var trimmed = email.Trim();
        if (trimmed.Any(char.IsWhiteSpace)) return null;

        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1) return null;

        var domain = trimmed[(at + 1)..].ToLowerInvariant();
        return domain.Contains('.') ? domain : null;
    }

    /// <summary>
    /// Public mailbox providers, which must never become one org's routing domain — that would send
    /// every Gmail user on the platform to a single customer's identity provider.
    /// </summary>
    private static readonly HashSet<string> ConsumerEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com",
        "yahoo.com", "yahoo.co.in", "yahoo.co.uk", "icloud.com", "me.com", "aol.com",
        "proton.me", "protonmail.com", "rediffmail.com", "zoho.com",
    };
}
