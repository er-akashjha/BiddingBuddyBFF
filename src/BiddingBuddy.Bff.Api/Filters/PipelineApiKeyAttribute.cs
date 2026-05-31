using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BiddingBuddy.Bff.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PipelineApiKeyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext ctx)
    {
        var config = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Pipeline:ApiKey"];

        if (string.IsNullOrWhiteSpace(expected)) return; // unconfigured = allow (dev)

        var provided = ctx.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (provided != expected)
        {
            ctx.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing X-Api-Key." });
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext ctx) { }
}
