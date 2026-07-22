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

    /// <summary>
    /// The EMD register. Every row carries a server-computed <c>verdict</c> (courier_late,
    /// not_couriered, refund_overdue, …) and a courier roll-up, and rows come back worst-first,
    /// so the client renders status rather than deriving it.
    /// </summary>
    /// <param name="status">held|submitted|pending|refunded|forfeited. Omit for all.</param>
    /// <param name="needsAction">Only deposits whose verdict is danger or warn.</param>
    /// <param name="q">Free text over tender title, bid title, GeM ref and instrument number.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Rows per page, clamped to 1–100.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("emd")]
    [ProducesResponseType(typeof(PagedResult<EmdPaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEmd(
        [FromQuery] string? status,
        [FromQuery] bool needsAction = false,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await paymentService.ListEmdAsync(CurrentOrgId, status, needsAction, q, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>
    /// Org-wide EMD totals for the register header — blocked capital, how many need action, and
    /// what is overdue for refund. Computed over every deposit, not the current page.
    /// </summary>
    [HttpGet("emd/summary")]
    [ProducesResponseType(typeof(EmdRegisterSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmdSummary(CancellationToken ct)
    {
        var summary = await paymentService.GetEmdSummaryAsync(CurrentOrgId, ct);
        return Ok(summary);
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

    /// <summary>
    /// Full record behind a register row: the deposit with its verdict, the bid it is attached
    /// to (stage, deadline, owner), and every consignment in dispatch order.
    /// </summary>
    [HttpGet("emd/{id:guid}/detail")]
    [ProducesResponseType(typeof(EmdDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmdDetail(Guid id, CancellationToken ct)
    {
        var detail = await paymentService.GetEmdDetailAsync(id, CurrentOrgId, ct);
        return Ok(detail);
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
