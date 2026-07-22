using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Payments;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class PaymentService(BffDbContext db) : IPaymentService
{
    // ── EMD ───────────────────────────────────────────────────────────────────

    public async Task<PagedResult<EmdPaymentDto>> ListEmdAsync(
        Guid orgId, string? status, bool needsAction, string? q, int page, int pageSize, CancellationToken ct = default)
    {
        var rows = await LoadRegisterRowsAsync(orgId, status, q, ct);

        // The verdict is computed, not stored, so "needs action" and the worst-first order can
        // only be applied after projection. The register is a per-org working set (tens to low
        // hundreds of rows), so materialising it is cheaper than the alternative — storing a
        // denormalised status that goes stale the moment a cut-off passes with nobody looking.
        if (needsAction)
            rows = rows.Where(r => r.Verdict!.Level is EmdVerdict.LevelDanger or EmdVerdict.LevelWarn).ToList();

        var ordered = rows
            .OrderBy(r => VerdictRank(r.Verdict!.Level))
            .ThenByDescending(r => r.Amount)
            .ToList();

        var pg = Math.Max(1, page);
        var sz = Math.Clamp(pageSize, 1, 100);
        var items = ordered.Skip((pg - 1) * sz).Take(sz).ToList();

        return new PagedResult<EmdPaymentDto>(items, ordered.Count, pg, sz);
    }

    public async Task<EmdRegisterSummaryDto> GetEmdSummaryAsync(Guid orgId, CancellationToken ct = default)
    {
        var rows = await LoadRegisterRowsAsync(orgId, status: null, q: null, ct);

        // "Blocked" is money the buyer is holding — submitted or held. Pending is not blocked
        // (nothing has left the account yet) and refunded/forfeited are settled.
        var blocked = rows.Where(r => r.Status is "submitted" or "held").ToList();
        var refundOverdue = rows.Where(r => r.Verdict!.Code == "refund_overdue").ToList();

        return new EmdRegisterSummaryDto(
            BlockedAmount:       blocked.Sum(r => r.Amount),
            BlockedCount:        blocked.Count,
            NeedsActionCount:    rows.Count(r => r.Verdict!.Level is EmdVerdict.LevelDanger or EmdVerdict.LevelWarn),
            CourierLateCount:    rows.Count(r => r.Verdict!.Code == "courier_late"),
            NotCouriedCount:     rows.Count(r => r.Verdict!.Code == "not_couriered"),
            RefundOverdueAmount: refundOverdue.Sum(r => r.Amount),
            RefundOverdueCount:  refundOverdue.Count,
            RefundedAmount:      rows.Where(r => r.Status == "refunded").Sum(r => r.RefundAmount ?? r.Amount),
            ForfeitedAmount:     rows.Where(r => r.Status == "forfeited").Sum(r => r.Amount));
    }

    public async Task<EmdPaymentDto> GetEmdAsync(Guid emdId, Guid orgId, CancellationToken ct = default)
    {
        var emd = await db.EmdPayments
            .Include(e => e.Bid)
            .FirstOrDefaultAsync(e => e.Id == emdId && e.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("EMD payment not found.");
        return MapEmd(emd, emd.Bid?.Title);
    }

    public async Task<EmdDetailDto> GetEmdDetailAsync(Guid emdId, Guid orgId, CancellationToken ct = default)
    {
        var emd = await db.EmdPayments
            .AsNoTracking()
            .Include(e => e.Bid).ThenInclude(b => b!.AssignedUser)
            .Include(e => e.Document)
            .FirstOrDefaultAsync(e => e.Id == emdId && e.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("EMD payment not found.");

        // Consignments hang off the BID, not the EMD — a dispatch created before the EMD record
        // existed has a null emd_payment_id but is still this deposit's courier. Matching on the
        // bid catches both; without it the timeline silently drops the early ones.
        var dispatches = emd.BidId is { } bidId
            ? await db.BidDispatches
                .AsNoTracking()
                .Where(d => d.BidId == bidId && d.OrgId == orgId
                         && (d.EmdPaymentId == null || d.EmdPaymentId == emd.Id))
                .Include(d => d.Dispatcher)
                .Include(d => d.PodDocument)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct)
            : [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = SummariseDispatches(dispatches, today);

        var dto = MapEmd(emd, emd.Bid?.Title) with
        {
            EmdRequirement = emd.Bid?.EmdRequirement,
            Dispatch = summary,
            Verdict = VerdictFor(emd, emd.Bid, summary, today),
        };

        var bidRef = emd.Bid is null ? null : new EmdBidRefDto(
            BidId:          emd.Bid.Id,
            Title:          emd.Bid.Title,
            Stage:          emd.Bid.Stage,
            StatusCategory: emd.Bid.StatusCategory,
            DueDate:        emd.Bid.DueDate,
            AssignedToName: emd.Bid.AssignedUser?.Name);

        return new EmdDetailDto(dto, bidRef, dispatches.Select(d => BidDispatchMapper.Map(d, today)).ToList());
    }

    // ── register plumbing ─────────────────────────────────────────────────────

    /// <summary>
    /// Every EMD for the org, each already carrying its courier roll-up and verdict. One query
    /// for the deposits and one for their consignments — never one per row.
    /// </summary>
    private async Task<List<EmdPaymentDto>> LoadRegisterRowsAsync(
        Guid orgId, string? status, string? q, CancellationToken ct)
    {
        var query = db.EmdPayments.AsNoTracking().Where(e => e.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.TenderTitle ?? "", term)
                || EF.Functions.ILike(e.InstrumentNumber ?? "", term)
                || EF.Functions.ILike(e.GemTenderId ?? "", term)
                || (e.Bid != null && EF.Functions.ILike(e.Bid.Title, term)));
        }

        var emds = await query.Include(e => e.Bid).ToListAsync(ct);
        if (emds.Count == 0) return [];

        var bidIds = emds.Where(e => e.BidId.HasValue).Select(e => e.BidId!.Value).Distinct().ToList();
        var dispatches = bidIds.Count == 0
            ? []
            : await db.BidDispatches.AsNoTracking()
                .Where(d => d.OrgId == orgId && bidIds.Contains(d.BidId))
                .ToListAsync(ct);

        var byBid = dispatches.GroupBy(d => d.BidId).ToDictionary(g => g.Key, g => g.ToList());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return emds.Select(e =>
        {
            var mine = e.BidId is { } bid && byBid.TryGetValue(bid, out var list)
                ? list.Where(d => d.EmdPaymentId == null || d.EmdPaymentId == e.Id).ToList()
                : [];
            var summary = SummariseDispatches(mine, today);

            return MapEmd(e, e.Bid?.Title) with
            {
                EmdRequirement = e.Bid?.EmdRequirement,
                Dispatch = summary,
                Verdict = VerdictFor(e, e.Bid, summary, today),
            };
        }).ToList();
    }

    private static EmdVerdictDto VerdictFor(
        EmdPayment e, Bid? bid, EmdDispatchSummaryDto summary, DateOnly today)
    {
        // A standalone EMD (no bid) has no recorded requirement, but it plainly exists — treat
        // it as required, or every one of them would read "not decided" forever.
        var requirement = bid?.EmdRequirement ?? EmdRequirements.Required;
        if (requirement == EmdRequirements.Unknown) requirement = EmdRequirements.Required;

        var r = EmdVerdict.For(new EmdVerdict.Input(
            Requirement:         requirement,
            BidIsOpen:           bid is null || bid.StatusCategory == "open",
            TenderEmdAmount:     null,
            ExemptionBasis:      bid?.EmdExemptionBasis,
            HasEmdRecord:        true,
            PaymentMode:         e.PaymentMode,
            Status:              e.Status,
            ValidUntil:          e.ValidUntil,
            PaymentDate:         e.PaymentDate,
            LiveDispatchCount:   summary.Live,
            AnyDispatchOverdue:  summary.AnyOverdue,
            AnyDispatchDelivered: summary.DeliveredOn is not null), today);

        return new EmdVerdictDto(r.Level, r.Code, r.Chip, r.Message);
    }

    private static EmdDispatchSummaryDto SummariseDispatches(List<BidDispatch> all, DateOnly today)
    {
        var outbound = all.Where(d => d.Direction == "outbound" && d.Purpose == "emd_instrument").ToList();
        var live = outbound.Where(d => !DispatchStatuses.Failed.Contains(d.Status)).ToList();
        var newest = live.OrderByDescending(d => d.DispatchedOn ?? DateOnly.MinValue)
                         .ThenByDescending(d => d.CreatedAt).FirstOrDefault();

        return new EmdDispatchSummaryDto(
            Total:          all.Count,
            Live:           live.Count,
            LatestStatus:   newest?.Status,
            CourierName:    newest?.CourierName,
            TrackingNumber: newest?.TrackingNumber,
            DeliverBy:      newest?.DeliverBy,
            DeliveredOn:    live.Select(d => d.DeliveredOn).FirstOrDefault(d => d is not null),
            AnyOverdue:     live.Any(d => BidDispatchMapper.IsOverdue(d, today)));
    }

    private static int VerdictRank(string level) => level switch
    {
        EmdVerdict.LevelDanger => 0,
        EmdVerdict.LevelWarn   => 1,
        EmdVerdict.LevelInfo   => 2,
        _                      => 3,
    };

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
