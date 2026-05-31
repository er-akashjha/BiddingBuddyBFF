using BiddingBuddy.Bff.Core.DTOs.Payments;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService paymentService) : BffControllerBase
{
    // ── EMD ───────────────────────────────────────────────────────────────────

    /// <summary>GET /api/payments/emd?status=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet("emd")]
    public async Task<IActionResult> ListEmd(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await paymentService.ListEmdAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>GET /api/payments/emd/{id}</summary>
    [HttpGet("emd/{id:guid}")]
    public async Task<IActionResult> GetEmd(Guid id, CancellationToken ct)
    {
        var emd = await paymentService.GetEmdAsync(id, CurrentOrgId, ct);
        return Ok(emd);
    }

    /// <summary>POST /api/payments/emd</summary>
    [HttpPost("emd")]
    public async Task<IActionResult> CreateEmd([FromBody] CreateEmdDto dto, CancellationToken ct)
    {
        var emd = await paymentService.CreateEmdAsync(CurrentOrgId, dto, ct);
        return Ok(emd);
    }

    /// <summary>PATCH /api/payments/emd/{id}</summary>
    [HttpPatch("emd/{id:guid}")]
    public async Task<IActionResult> UpdateEmd(Guid id, [FromBody] UpdateEmdDto dto, CancellationToken ct)
    {
        var emd = await paymentService.UpdateEmdAsync(id, CurrentOrgId, dto, ct);
        return Ok(emd);
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    /// <summary>GET /api/payments/invoices?status=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet("invoices")]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await paymentService.ListInvoicesAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>GET /api/payments/invoices/{id}</summary>
    [HttpGet("invoices/{id:guid}")]
    public async Task<IActionResult> GetInvoice(Guid id, CancellationToken ct)
    {
        var invoice = await paymentService.GetInvoiceAsync(id, CurrentOrgId, ct);
        return Ok(invoice);
    }

    /// <summary>POST /api/payments/invoices</summary>
    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceDto dto, CancellationToken ct)
    {
        var invoice = await paymentService.CreateInvoiceAsync(CurrentOrgId, dto, ct);
        return Ok(invoice);
    }

    /// <summary>PATCH /api/payments/invoices/{id}</summary>
    [HttpPatch("invoices/{id:guid}")]
    public async Task<IActionResult> UpdateInvoice(Guid id, [FromBody] UpdateInvoiceDto dto, CancellationToken ct)
    {
        var invoice = await paymentService.UpdateInvoiceAsync(id, CurrentOrgId, dto, ct);
        return Ok(invoice);
    }
}
