using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Payments;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
[Produces("application/json")]
public class PaymentsController(IPaymentService paymentService) : BffControllerBase
{
    // ── EMD Payments ──────────────────────────────────────────────────────────

    /// <summary>Paginated list of EMD payments. Filter by status (held|refunded|forfeited).</summary>
    [HttpGet("emd")]
    [ProducesResponseType(typeof(PagedResult<EmdPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEmd(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await paymentService.ListEmdAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Get a single EMD payment record.</summary>
    [HttpGet("emd/{id:guid}")]
    [ProducesResponseType(typeof(EmdPaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmd(Guid id, CancellationToken ct)
    {
        var emd = await paymentService.GetEmdAsync(id, CurrentOrgId, ct);
        return Ok(emd);
    }

    /// <summary>Record a new EMD payment.</summary>
    [HttpPost("emd")]
    [ProducesResponseType(typeof(EmdPaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateEmd([FromBody] CreateEmdDto dto, CancellationToken ct)
    {
        var emd = await paymentService.CreateEmdAsync(CurrentOrgId, dto, ct);
        return Ok(emd);
    }

    /// <summary>Update EMD status, refund details or notes.</summary>
    [HttpPatch("emd/{id:guid}")]
    [ProducesResponseType(typeof(EmdPaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEmd(Guid id, [FromBody] UpdateEmdDto dto, CancellationToken ct)
    {
        var emd = await paymentService.UpdateEmdAsync(id, CurrentOrgId, dto, ct);
        return Ok(emd);
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    /// <summary>Paginated list of invoices. Filter by status (pending|paid|overdue|cancelled).</summary>
    [HttpGet("invoices")]
    [ProducesResponseType(typeof(PagedResult<InvoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await paymentService.ListInvoicesAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Get a single invoice record.</summary>
    [HttpGet("invoices/{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken ct)
    {
        var invoice = await paymentService.GetInvoiceAsync(id, CurrentOrgId, ct);
        return Ok(invoice);
    }

    /// <summary>Create a new invoice, optionally linked to an order.</summary>
    [HttpPost("invoices")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        var invoice = await paymentService.CreateInvoiceAsync(CurrentOrgId, dto, ct);
        return Ok(invoice);
    }

    /// <summary>Update invoice fields (status, paid amount, payment reference, due date).</summary>
    [HttpPatch("invoices/{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
    {
        var invoice = await paymentService.UpdateInvoiceAsync(id, CurrentOrgId, dto, ct);
        return Ok(invoice);
    }
}
