using System.Security.Claims;
using BiddingBuddy.Bff.Core.Interfaces;

namespace BiddingBuddy.Bff.Api.Middleware;

/// <summary>
/// Validates X-Org-Id header and confirms the calling user is an active member.
/// Sets HttpContext.Items["OrgId"] for controllers.
/// Skipped for /api/auth/* and /internal/* routes.
/// </summary>
public class OrgContextMiddleware(RequestDelegate next)
{
    private static readonly string[] SkipPrefixes = ["/api/auth", "/internal", "/swagger", "/health"];

    public async Task InvokeAsync(HttpContext ctx, IOrganizationRepository orgRepo)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (SkipPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
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
