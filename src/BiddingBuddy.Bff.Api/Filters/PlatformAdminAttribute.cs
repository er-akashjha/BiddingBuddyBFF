using System.Security.Claims;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BiddingBuddy.Bff.Api.Filters;

/// <summary>
/// Requires the calling user to be a platform operator (<c>users.is_platform_admin</c>). Gates the
/// JWT-authenticated <c>/api/admin/*</c> surface — the in-app counterpart to the <see cref="PipelineApiKeyAttribute"/>
/// <c>/internal/*</c> routes, so an operator can work the buyer-access queue with their own identity
/// instead of an out-of-band API key.
///
/// <para><b>Checked against the database on every call, not against a JWT claim.</b> The flag is rare
/// and slow-changing, the endpoints behind it are low-traffic, and a claim would let a revoked
/// operator keep admin access until their 15-minute access token expired. A per-request lookup keeps
/// "who is an operator" answerable in exactly one place — the row — with no staleness window.</para>
///
/// <para>Fails closed. No parseable user id → 401 (not 403): the caller has not proven who they are,
/// which is a different problem from proving it and lacking the flag. A known user without the flag →
/// 403 with <c>code: "FORBIDDEN_PLATFORM_ADMIN"</c>, mirroring how <see cref="RequireOrgCapabilityAttribute"/>
/// tags its own refusal so the SPA can tell an authorization verdict from a dead session.</para>
///
/// <para>Reads identity straight from the JWT claims rather than <c>HttpContext.Items</c>: admin routes
/// are exempt from <c>OrgContextMiddleware</c> (they are not org-scoped), so nothing has stashed a
/// <c>UserId</c> for this filter to lean on.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PlatformAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var http = ctx.HttpContext;

        var sub = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? http.User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            ctx.Result = new UnauthorizedObjectResult(new { error = "Authentication is required." });
            return;
        }

        var userRepo = http.RequestServices.GetRequiredService<IUserRepository>();
        var user = await userRepo.FindByIdAsync(userId, http.RequestAborted);

        if (user is null || !user.IsPlatformAdmin)
        {
            ctx.Result = new ObjectResult(new
            {
                error = "This action is limited to platform operators.",
                code  = "FORBIDDEN_PLATFORM_ADMIN",
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
