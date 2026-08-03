using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BiddingBuddy.Bff.Api.Filters;

/// <summary>
/// Requires the calling org's plan to include a <see cref="PlanFeatures"/> feature.
///
/// Runs after <c>OrgContextMiddleware</c> (membership) and alongside
/// <c>RequireOrgCapabilityAttribute</c> (role). This is the third axis: membership says
/// you are in the room, a capability says your role may touch this, a plan feature says
/// your organization has paid for it.
///
/// <para><b>The 403 is distinguishable on purpose</b> — <c>code: "UPGRADE_REQUIRED"</c>
/// plus the missing feature and the plan that would unlock it, so the SPA renders an
/// upgrade prompt instead of a permissions error, and (critically) does not treat it as
/// a dead org membership.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePlanFeatureAttribute(string feature) : Attribute, IAsyncAuthorizationFilter
{
    public string Feature { get; } = feature;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        // A method-level attribute overrides a controller-level one — same rule as
        // RequireOrgCapabilityAttribute, for the same reason.
        var effective = ctx.ActionDescriptor.EndpointMetadata
            .OfType<RequirePlanFeatureAttribute>()
            .LastOrDefault() ?? this;
        if (!ReferenceEquals(effective, this)) return;

        var http = ctx.HttpContext;

        if (http.Items["OrgId"] is not Guid orgId)
        {
            // Only reachable when a middleware-skipped route is decorated — a wiring bug.
            ctx.Result = new ObjectResult(new
            {
                error = "Organization context is required for this endpoint but was not established.",
                code  = "ORG_CONTEXT_MISSING",
            })
            { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }

        var plans = http.RequestServices.GetRequiredService<IPlanService>();
        var plan = await plans.GetPlanForAsync(orgId, http.RequestAborted);

        if (!plan.HasFeature(Feature))
        {
            ctx.Result = new ObjectResult(new
            {
                error        = "Your current plan does not include this feature.",
                code         = "UPGRADE_REQUIRED",
                feature      = Feature,
                requiredPlan = PlanCatalog.LowestPlanWith(Feature),
                currentPlan  = plan.PlanCode,
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
