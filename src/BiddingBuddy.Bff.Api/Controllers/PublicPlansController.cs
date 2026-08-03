using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Billing;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// The plan catalog for the public pricing page. Anonymous — /api/public/* is on the
/// org-middleware skip list. Serves the same <see cref="PlanCatalog"/> the gates compile
/// against, so published prices and enforced limits cannot drift apart.
/// </summary>
[ApiController]
[Route("api/public/plans")]
[AllowAnonymous]
[EnableRateLimiting("public")]
[Produces("application/json")]
public class PublicPlansController(IOptions<RazorpayOptions> razorpay) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PublicPlansDto), StatusCodes.Status200OK)]
    public IActionResult GetPlans()
        => Ok(new PublicPlansDto(
            PlanCatalog.All.Select(p => new PublicPlanDto(
                p.Code, p.Name, p.PricePaiseAnnual, p.AnchorPricePaiseAnnual,
                p.SeatCap, p.AiSummariesPerMonth, p.SavedFilterCap, p.AlertFloorMinutes,
                p.Features.ToList(), p.IsPopular, p.Tagline, p.Bullets)).ToList(),
            razorpay.Value.IsConfigured));
}
