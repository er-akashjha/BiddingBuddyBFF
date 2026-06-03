using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BiddingBuddy.Bff.Api.Swagger;

/// <summary>
/// Adds the X-Org-Id header parameter to every endpoint that goes through
/// OrgContextMiddleware (i.e. everything except /api/auth/*, /internal/*, /swagger/*).
/// </summary>
public sealed class OrgIdHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;

        if (path.StartsWith("api/auth",   StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("internal",   StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("swagger",    StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("health",     StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name        = "X-Org-Id",
            In          = ParameterLocation.Header,
            Required    = true,
            Description = "UUID of the active organization. "  +
                          "Admin org: 87aa0e86-4200-47f5-87e1-cb17ff4d5fa1",
            Schema      = new OpenApiSchema { Type = "string", Format = "uuid" },
        });
    }
}

/// <summary>
/// Adds the X-Api-Key header parameter to /internal/* endpoints.
/// </summary>
public sealed class ApiKeyHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;

        if (!path.StartsWith("internal", StringComparison.OrdinalIgnoreCase))
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name        = "X-Api-Key",
            In          = ParameterLocation.Header,
            Required    = true,
            Description = "Pipeline API key — value from Pipeline:ApiKey in appsettings.",
            Schema      = new OpenApiSchema { Type = "string" },
        });
    }
}
