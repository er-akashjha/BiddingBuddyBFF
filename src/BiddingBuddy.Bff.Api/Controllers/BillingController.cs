using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Authorization;
using BiddingBuddy.Bff.Core.DTOs.Billing;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Subscription billing for the calling organization.
///
/// Named Billing, not Payments: <c>PaymentsController</c> is the TENDER domain (EMD
/// deposits and invoices the org raises to government buyers). Nothing here touches that.
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize]
[Produces("application/json")]
public class BillingController(IBillingService billing, IPromoService promos) : BffControllerBase
{
    /// <summary>Current plan, status, trial/renewal dates, seat + AI usage. Any member.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(BillingSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await billing.GetSummaryAsync(CurrentOrgId, ct));

    /// <summary>
    /// Preview a promo code against a plan. Always 200 — an unusable code comes back as
    /// <c>{ valid: false, error: "EXPIRED" | "ALREADY_USED" | … }</c>, which the UI turns
    /// into an inline message. Every money figure is computed here, never on the client.
    /// </summary>
    [HttpPost("promo/validate")]
    [RequireOrgCapability(OrgCapabilities.BillingManage)]
    [ProducesResponseType(typeof(PromoValidationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidatePromo([FromBody] ValidatePromoDto dto, CancellationToken ct)
        => Ok(await promos.ValidateAsync(CurrentOrgId, dto.Code ?? string.Empty, dto.PlanCode, ct));

    /// <summary>
    /// Create a Razorpay order for a plan (optionally with a promo code) and return what
    /// the browser checkout needs. 503 CHECKOUT_UNAVAILABLE while Razorpay keys are
    /// unconfigured — the UI shows "Contact us" instead.
    /// </summary>
    [HttpPost("checkout")]
    [RequireOrgCapability(OrgCapabilities.BillingManage)]
    [ProducesResponseType(typeof(CheckoutOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateCheckout([FromBody] CreateCheckoutDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await billing.CreateCheckoutAsync(CurrentOrgId, CurrentUserId, dto, ct));
        }
        catch (InvalidOperationException ex) when (ex.Message == "CHECKOUT_UNAVAILABLE")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Online payment is not available yet. Please contact us to activate your plan.",
                code  = "CHECKOUT_UNAVAILABLE",
            });
        }
        catch (InvalidOperationException ex)
        {
            // Promo failures (EXPIRED / ALREADY_USED / …) and plan/cycle validation.
            return BadRequest(new { error = "Checkout could not be started.", code = ex.Message });
        }
    }

    /// <summary>
    /// Razorpay checkout handler callback. Verifies the signature, then activates —
    /// idempotently, since the webhook races this and either may win.
    /// </summary>
    [HttpPost("verify")]
    [RequireOrgCapability(OrgCapabilities.BillingManage)]
    [ProducesResponseType(typeof(BillingSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify([FromBody] VerifyPaymentDto dto, CancellationToken ct)
        => Ok(await billing.VerifyAndActivateAsync(CurrentOrgId, dto, ct));

    /// <summary>Subscription payment history for this org (not GeM order invoices).</summary>
    [HttpGet("payments")]
    [RequireOrgCapability(OrgCapabilities.BillingManage)]
    [ProducesResponseType(typeof(IReadOnlyList<BillingPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayments(CancellationToken ct)
        => Ok(await billing.GetPaymentsAsync(CurrentOrgId, ct));
}
