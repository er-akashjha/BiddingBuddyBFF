using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

public abstract class BffControllerBase : ControllerBase
{
    protected Guid CurrentUserId
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub")
                   ?? throw new UnauthorizedAccessException("User ID not found in token.");
            return Guid.Parse(sub);
        }
    }

    protected Guid CurrentOrgId
        => HttpContext.Items["OrgId"] is Guid id
            ? id
            : throw new InvalidOperationException("OrgId not set by middleware.");
}
