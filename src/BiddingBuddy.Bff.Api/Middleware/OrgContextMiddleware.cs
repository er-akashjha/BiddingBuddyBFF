using System.Security.Claims;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Api.Middleware;

/// <summary>
/// Validates X-Org-Id header and confirms the calling user is an active member.
/// Sets HttpContext.Items["OrgId"] for controllers.
/// Skipped for /api/auth/*, /api/public/* (anonymous guest browsing), and /internal/* routes.
/// </summary>
public class OrgContextMiddleware(RequestDelegate next)
{
    // /api/invites is exempt because the caller is by definition not yet a member
    // of the org the invite points at (accept/decline happen before membership).
    // /api/join-requests is exempt for the same reason pointing the other way: asking to
    // join an org is something only a non-member ever does.
    // /api/devices is exempt because a push device belongs to a user, not an org
    // (the mobile app registers its FCM token before/independent of any org context).
    private static readonly string[] SkipPrefixes = ["/api/auth", "/api/public", "/api/invites", "/api/join-requests", "/api/devices", "/internal", "/swagger", "/health", "/sitemap"];

    public async Task InvokeAsync(HttpContext ctx, IOrganizationRepository orgRepo)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx);
            return;
        }

        // Creating your very first organization is inherently pre-org — a social
        // signup has no X-Org-Id to send yet. Exempt exactly POST /api/organizations
        // (the collection route only; subroutes stay org-scoped).
        if (HttpMethods.IsPost(ctx.Request.Method) &&
            path.TrimEnd('/').Equals("/api/organizations", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        // Only enforce on authenticated requests
        if (!ctx.User.Identity?.IsAuthenticated ?? true)
        {
            await next(ctx);
            return;
        }

        var orgIdHeader = ctx.Request.Headers["X-Org-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(orgIdHeader) || !Guid.TryParse(orgIdHeader, out var orgId))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "X-Org-Id header is required and must be a valid UUID." });
            return;
        }

        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid user identity." });
            return;
        }

        var isMember = await orgRepo.IsUserMemberAsync(orgId, userId);
        if (!isMember)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "You are not a member of this organization." });
            return;
        }

        ctx.Items["OrgId"] = orgId;
        ctx.Items["UserId"] = userId;
        await next(ctx);
    }
}
