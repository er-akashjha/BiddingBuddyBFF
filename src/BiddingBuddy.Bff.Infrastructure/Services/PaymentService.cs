using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Payments;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class PaymentService(BffDbContext db) : IPaymentService
{
    // ── EMD ───────────────────────────────────────────────────────────────────

    public async Task<PagedResult<EmdPaymentDto>> ListEmdAsync(
        Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.EmdPayments.Where(e => e.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);

        var total = await query.CountAsync(ct);
        var pg = Math.Max(1, page);
        var sz = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .Include(e => e.Bid)
            .OrderByDescending(e => e.PaymentDate)
            .Skip((pg - 1) * sz).Take(sz)
            .Select(e => MapEmd(e, e.Bid != null ? e.Bid.Title : null))
            .ToListAsync(ct);

        return new PagedResult<EmdPaymentDto>(items, total, pg, sz);
    }

    public async Task<EmdPaymentDto> GetEmdAsync(Guid emdId, Guid orgId, CancellationToken ct = default)
    {
        var emd = await db.EmdPayments
            .Include(e => e.Bid)
            .FirstOrDefaultAsync(e => e.Id == emdId && e.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("EMD payment not found.");
        return MapEmd(emd, emd.Bid?.Title);
    }

    public async Task<EmdPaymentDto> CreateEmdAsync(Guid orgId, CreateEmdDto dto, CancellationToken ct = default)
    {
        var emd = new EmdPayment
        {
            OrgId          = orgId,
            BidId          = dto.BidId,
            TenderId       = dto.TenderId,
            GemTenderId    = dto.GemTenderId,
            TenderTitle    = dto.TenderTitle,
            Amount         = dto.Amount,
            PaymentDate    = dto.PaymentDate,
            PaymentMode    = dto.PaymentMode,
            TransactionRef = dto.TransactionRef,
            BankName       = dto.BankName,
            Notes          = dto.Notes,
            Status         = "held",
        };
        db.EmdPayments.Add(emd);
        await db.SaveChangesAsync(ct);
        return MapEmd(emd, null);
    }

    public async Task<EmdPaymentDto> UpdateEmdAsync(Guid emdId, Guid orgId, UpdateEmdDto dto, CancellationToken ct = default)
    {
        var emd = await db.EmdPayments
            .Include(e => e.Bid)
            .FirstOrDefaultAsync(e => e.Id == emdId && e.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("EMD payment not found.");

        if (dto.PaymentMode    is not null) emd.PaymentMode    = dto.PaymentMode;
        if (dto.TransactionRef is not null) emd.TransactionRef = dto.TransactionRef;
        if (dto.BankName       is not null) emd.BankName       = dto.BankName;
        if (dto.Status         is not null) emd.Status         = dto.Status;
        if (dto.RefundDate.HasValue)        emd.RefundDate     = dto.RefundDate;
        if (dto.RefundAmount.HasValue)      emd.RefundAmount   = dto.RefundAmount;
        if (dto.RefundRef      is not null) emd.RefundRef      = dto.RefundRef;
        if (dto.Notes          is not null) emd.Notes          = dto.Notes;

        await db.SaveChangesAsync(ct);
        return MapEmd(emd, emd.Bid?.Title);
    }

    // ── Invoices ──────────────────────────────────────────────────────────────

    public async Task<PagedResult<InvoiceDto>> ListInvoicesAsync(
        Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Invoices.Where(i => i.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);

        var total = await query.CountAsync(ct);
        var pg = Math.Max(1, page);
        var sz = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .Include(i => i.Order)
            .OrderByDescending(i => i.InvoiceDate)
            .Skip((pg - 1) * sz).Take(sz)
            .Select(i => MapInvoice(i, i.Order != null ? i.Order.OrderNumber : null))
            .ToListAsync(ct);

        return new PagedResult<InvoiceDto>(items, total, pg, sz);
    }

    public async Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId, Guid orgId, CancellationToken ct = default)
    {
        var inv = await db.Invoices
            .Include(i => i.Order)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Invoice not found.");
        return MapInvoice(inv, inv.Order?.OrderNumber);
    }

    public async Task<InvoiceDto> CreateInvoiceAsync(Guid orgId, CreateInvoiceDto dto, CancellationToken ct = default)
    {
        var inv = new Invoice
        {
            OrgId          = orgId,
            OrderId        = dto.OrderId,
            InvoiceNumber  = dto.InvoiceNumber,
            BuyerOrg       = dto.BuyerOrg,
            Amount         = dto.Amount,
            GstAmount      = dto.GstAmount,
            TotalAmount    = dto.Amount + (dto.GstAmount ?? 0),
            InvoiceDate    = dto.InvoiceDate,
            DueDate        = dto.DueDate,
            Notes          = dto.Notes,
            Status         = "pending",
        };
        db.Invoices.Add(inv);
        await db.SaveChangesAsync(ct);
        return MapInvoice(inv, null);
    }

    public async Task<InvoiceDto> UpdateInvoiceAsync(Guid invoiceId, Guid orgId, UpdateInvoiceDto dto, CancellationToken ct = default)
    {
        var inv = await db.Invoices
            .Include(i => i.Order)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Invoice not found.");

        if (dto.InvoiceNumber is not null) inv.InvoiceNumber = dto.InvoiceNumber;
        if (dto.BuyerOrg      is not null) inv.BuyerOrg      = dto.BuyerOrg;
        if (dto.Amount.HasValue)           inv.Amount        = dto.Amount.Value;
        if (dto.GstAmount.HasValue)        inv.GstAmount     = dto.GstAmount;
        if (dto.TotalAmount.HasValue)      inv.TotalAmount   = dto.TotalAmount;
        if (dto.InvoiceDate.HasValue)      inv.InvoiceDate   = dto.InvoiceDate.Value;
        if (dto.DueDate.HasValue)          inv.DueDate       = dto.DueDate;
        if (dto.PaidDate.HasValue)         inv.PaidDate      = dto.PaidDate;
        if (dto.PaidAmount.HasValue)       inv.PaidAmount    = dto.PaidAmount;
        if (dto.Status        is not null) inv.Status        = dto.Status;
        if (dto.PaymentRef    is not null) inv.PaymentRef    = dto.PaymentRef;
        if (dto.Notes         is not null) inv.Notes         = dto.Notes;

        await db.SaveChangesAsync(ct);
        return MapInvoice(inv, inv.Order?.OrderNumber);
    }

    private static EmdPaymentDto MapEmd(EmdPayment e, string? bidTitle)
        => new(e.Id, e.BidId, bidTitle, e.TenderId, e.GemTenderId, e.TenderTitle,
            e.Amount, e.PaymentDate, e.PaymentMode, e.TransactionRef, e.BankName,
            e.Status, e.RefundDate, e.RefundAmount, e.RefundRef, e.Notes, e.UpdatedAt);

    private static InvoiceDto MapInvoice(Invoice i, string? orderNumber)
        => new(i.Id, i.OrderId, orderNumber, i.InvoiceNumber, i.BuyerOrg,
            i.Amount, i.GstAmount, i.TotalAmount,
            i.InvoiceDate, i.DueDate, i.PaidDate, i.PaidAmount,
            i.Status, i.PaymentRef, i.Notes, i.UpdatedAt);
}
