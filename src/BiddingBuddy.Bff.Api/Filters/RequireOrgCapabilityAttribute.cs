using BiddingBuddy.Bff.Core.Authorization;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BiddingBuddy.Bff.Api.Filters;

/// <summary>
/// Requires the calling user to hold a capability in the organization named by <c>X-Org-Id</c>.
///
/// Runs after <c>OrgContextMiddleware</c>, which has already proved membership and stashed
/// <c>OrgId</c>/<c>UserId</c>. This filter answers the question the middleware deliberately does
/// not: membership says you are in the room, a capability says you may touch this.
///
/// <para><b>The 403 here is distinguishable from the membership 403 on purpose.</b> Those two have
/// been indistinguishable to clients, so the SPA cannot tell "you were removed from this org"
/// (re-validate the session, switch org) from "you lack the role" (show a permissions message) and
/// has to guess by re-fetching <c>/api/auth/me</c>. Every response from this filter carries
/// <c>code: "FORBIDDEN_ROLE"</c> plus the capability that was missing and the role actually held,
/// so the client can branch without a second round-trip.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireOrgCapabilityAttribute(string capability) : Attribute, IAsyncAuthorizationFilter
{
    public string Capability { get; } = capability;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        // A method-level attribute overrides a controller-level one. Without this, decorating a
        // controller with tender.read and one action with tender.publish would enforce BOTH, and
        // the stricter would silently win — a rule nobody reading the action would predict.
        var effective = ctx.ActionDescriptor.EndpointMetadata
            .OfType<RequireOrgCapabilityAttribute>()
            .LastOrDefault() ?? this;
        if (!ReferenceEquals(effective, this)) return;

        var http = ctx.HttpContext;

        if (http.Items["OrgId"] is not Guid orgId || http.Items["UserId"] is not Guid userId)
        {
            // Reachable only if a route that skips OrgContextMiddleware is decorated with this
            // attribute. That is a wiring bug, not a client error — say so rather than 403ing,
            // which would send someone hunting for a permissions problem that does not exist.
            ctx.Result = new ObjectResult(new
            {
                error = "Organization context is required for this endpoint but was not established.",
                code  = "ORG_CONTEXT_MISSING",
            })
            { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }

        var orgRepo = http.RequestServices.GetRequiredService<IOrganizationRepository>();
        var role = await orgRepo.GetUserRoleAsync(orgId, userId, http.RequestAborted);

        if (!OrgCapabilities.Has(role, Capability))
        {
            ctx.Result = new ObjectResult(new
            {
                error      = $"Your role does not permit this action.",
                code       = "FORBIDDEN_ROLE",
                capability = Capability,
                role       = role ?? "none",
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        // Handed to controllers so an action can record the acting role in the audit trail
        // without a second lookup. The audit file names the role a change was made under, and
        // re-reading it later would report today's role rather than the one in force at the time.
        http.Items["OrgRole"] = role;
    }
}
