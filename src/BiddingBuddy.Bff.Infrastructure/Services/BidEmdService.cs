using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Payments;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Helpers;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// EMD + courier tracking for a bid. See <see cref="IBidEmdService"/> for the contract.
/// <para>
/// The EMD row is written to <c>emd_payments</c> — shared with the finance register — rather
/// than a bid-local table, so there is exactly one EMD per bid no matter which screen created
/// it, and the existing EMD_STUCK refund chase keeps working on rows this feature makes.
/// </para>
/// </summary>
public class BidEmdService(BffDbContext db) : IBidEmdService
{
    public async Task<BidEmdDto> GetAsync(Guid orgId, Guid bidId, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);
        var emd = await LoadEmdAsync(bidId, orgId, ct);
        var dispatches = await LoadDispatchesAsync(bidId, orgId, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var live = dispatches
            .Where(d => d.Direction == "outbound" && d.Purpose == "emd_instrument"
                     && !DispatchStatuses.Failed.Contains(d.Status))
            .ToList();

        var v = EmdVerdict.For(new EmdVerdict.Input(
            Requirement:          bid.EmdRequirement,
            BidIsOpen:            bid.StatusCategory == "open",
            TenderEmdAmount:      bid.Tender?.EmdAmount,
            ExemptionBasis:       bid.EmdExemptionBasis,
            HasEmdRecord:         emd is not null,
            PaymentMode:          emd?.PaymentMode,
            Status:               emd?.Status,
            ValidUntil:           emd?.ValidUntil,
            PaymentDate:          emd?.PaymentDate,
            LiveDispatchCount:    live.Count,
            AnyDispatchOverdue:   live.Any(d => d.IsOverdue),
            AnyDispatchDelivered: live.Any(d => d.DeliveredOn is not null)), today);

        return new BidEmdDto(
            Requirement:     bid.EmdRequirement,
            ExemptionBasis:  bid.EmdExemptionBasis,
            TenderEmdAmount: bid.Tender?.EmdAmount,
            Emd:             emd is null ? null : MapEmd(emd),
            Dispatches:      dispatches,
            Verdict:         new EmdVerdictDto(v.Level, v.Code, v.Chip, v.Message));
    }

    public async Task<BidEmdDto> SaveAsync(
        Guid orgId, Guid bidId, Guid userId, SaveBidEmdDto dto, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);

        if (dto.Requirement is not null)
        {
            if (!EmdRequirements.IsValid(dto.Requirement))
                throw new ArgumentException($"Unknown EMD requirement '{dto.Requirement}'.");
            bid.EmdRequirement = dto.Requirement;
        }
        if (dto.ExemptionBasis is not null) bid.EmdExemptionBasis = dto.ExemptionBasis;

        if (dto.PaymentMode is not null && !IsKnownMode(dto.PaymentMode))
            throw new ArgumentException($"Unknown EMD payment mode '{dto.PaymentMode}'.");

        var emd = await LoadEmdAsync(bidId, orgId, ct);

        // An exemption or an EMD-free tender has no money and no paper. Creating a zero-amount
        // row for it would put a phantom EMD in the finance register and arm EMD_STUCK against
        // something that was never paid.
        var wantsRecord = bid.EmdRequirement is EmdRequirements.Required or EmdRequirements.Unknown
                          && HasAnyEmdField(dto);

        if (emd is null && wantsRecord)
        {
            emd = new EmdPayment
            {
                OrgId       = orgId,
                // Navigation, NOT BidId = bid.Id. Both rows can be new inside one SaveChanges
                // (a bid created and its EMD recorded in the same request), and the scalar form
                // leaves EF free to order the INSERTs wrong — which surfaces as a 23503 FK
                // violation at runtime and is invisible to the in-memory test provider.
                Bid         = bid,
                TenderId    = bid.TenderId,
                GemTenderId = bid.Tender?.GemTenderId,
                TenderTitle = bid.Tender?.Title ?? bid.Title,
                Amount      = dto.Amount ?? bid.Tender?.EmdAmount ?? 0m,
                PaymentDate = dto.PaymentDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Status      = dto.Status ?? "pending",
            };
            db.EmdPayments.Add(emd);
        }

        if (emd is not null)
        {
            if (dto.Amount.HasValue)              emd.Amount           = dto.Amount.Value;
            if (dto.PaymentDate.HasValue)         emd.PaymentDate      = dto.PaymentDate.Value;
            if (dto.PaymentMode      is not null) emd.PaymentMode      = dto.PaymentMode;
            if (dto.TransactionRef   is not null) emd.TransactionRef   = dto.TransactionRef;
            if (dto.BankName         is not null) emd.BankName         = dto.BankName;
            if (dto.Status           is not null) emd.Status           = dto.Status;
            if (dto.InstrumentNumber is not null) emd.InstrumentNumber = dto.InstrumentNumber;
            if (dto.InstrumentDate.HasValue)      emd.InstrumentDate   = dto.InstrumentDate;
            if (dto.ValidUntil.HasValue)          emd.ValidUntil       = dto.ValidUntil;
            if (dto.IssuingBranch    is not null) emd.IssuingBranch    = dto.IssuingBranch;
            if (dto.Favouring        is not null) emd.Favouring        = dto.Favouring;
            if (dto.DueDate.HasValue)             emd.DueDate          = dto.DueDate;
            if (dto.RefundDate.HasValue)          emd.RefundDate       = dto.RefundDate;
            if (dto.RefundAmount.HasValue)        emd.RefundAmount     = dto.RefundAmount;
            if (dto.RefundRef        is not null) emd.RefundRef        = dto.RefundRef;
            if (dto.Notes            is not null) emd.Notes            = dto.Notes;

            if (dto.DocumentId.HasValue)
                emd.DocumentId = await ResolveOrgDocumentAsync(dto.DocumentId.Value, orgId, ct);

            // The EMD due date defaults to the bid's own deadline — in practice the EMD has to
            // be in place by submission, and making the user retype a date we already hold is
            // how it ends up blank and unalertable.
            emd.DueDate ??= bid.DueDate;
        }

        db.BidActivities.Add(new BidActivity
        {
            BidId   = bid.Id,
            ActorId = userId,
            Action  = "emd_updated",
            ToValue = bid.EmdRequirement,
        });

        await db.SaveChangesAsync(ct);
        return await GetAsync(orgId, bidId, ct);
    }

    public async Task<BidDispatchDto> CreateDispatchAsync(
        Guid orgId, Guid bidId, Guid userId, CreateBidDispatchDto dto, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);

        if (dto.Status is not null && !DispatchStatuses.IsValid(dto.Status))
            throw new ArgumentException($"Unknown dispatch status '{dto.Status}'.");

        // Default-link to the bid's EMD so the courier and the money it carries stay associated
        // without the client having to know the EMD's id. Already-persisted row, so the scalar
        // FK is safe here — unlike the EMD create above.
        var emdId = dto.EmdPaymentId;
        if (emdId is null && dto.Purpose == "emd_instrument")
            emdId = (await LoadEmdAsync(bidId, orgId, ct))?.Id;
        else if (emdId is not null)
            emdId = await ResolveOrgEmdAsync(emdId.Value, orgId, ct);

        var dispatch = new BidDispatch
        {
            OrgId                = orgId,
            Bid                  = bid,          // navigation — see the note in SaveAsync
            EmdPaymentId         = emdId,
            Purpose              = dto.Purpose,
            Direction            = dto.Direction,
            CourierName          = dto.CourierName,
            TrackingNumber       = dto.TrackingNumber,
            TrackingUrl          = dto.TrackingUrl,
            DispatchedOn         = dto.DispatchedOn,
            DispatchedBy         = userId,
            RecipientName        = dto.RecipientName,
            RecipientDesignation = dto.RecipientDesignation,
            RecipientAddress     = dto.RecipientAddress,
            RecipientPhone       = dto.RecipientPhone,
            // Falls back to the bid deadline for the same reason the EMD due date does: the
            // cut-off is nearly always bid submission, and a blank one silences the alerts.
            DeliverBy            = dto.DeliverBy ?? bid.DueDate,
            ExpectedDeliveryOn   = dto.ExpectedDeliveryOn,
            Notes                = dto.Notes,
            // A row with a dispatch date is by definition already handed over; defaulting it to
            // 'draft' would leave it invisible to the "courier is late" scan.
            Status               = dto.Status
                                   ?? (dto.DispatchedOn.HasValue ? DispatchStatuses.Dispatched : DispatchStatuses.Draft),
        };
        db.BidDispatches.Add(dispatch);

        db.BidActivities.Add(new BidActivity
        {
            BidId   = bid.Id,
            ActorId = userId,
            Action  = "emd_dispatched",
            ToValue = dto.CourierName,
            Note    = dto.TrackingNumber,
        });

        await db.SaveChangesAsync(ct);
        return await GetDispatchDtoAsync(dispatch.Id, orgId, ct);
    }

    public async Task<BidDispatchDto> UpdateDispatchAsync(
        Guid orgId, Guid bidId, Guid dispatchId, Guid userId, UpdateBidDispatchDto dto, CancellationToken ct = default)
    {
        var d = await db.BidDispatches
            .FirstOrDefaultAsync(x => x.Id == dispatchId && x.BidId == bidId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Dispatch not found.");

        if (dto.Status is not null && !DispatchStatuses.IsValid(dto.Status))
            throw new ArgumentException($"Unknown dispatch status '{dto.Status}'.");

        if (dto.CourierName          is not null) d.CourierName          = dto.CourierName;
        if (dto.TrackingNumber       is not null) d.TrackingNumber       = dto.TrackingNumber;
        if (dto.TrackingUrl          is not null) d.TrackingUrl          = dto.TrackingUrl;
        if (dto.DispatchedOn.HasValue)            d.DispatchedOn         = dto.DispatchedOn;
        if (dto.RecipientName        is not null) d.RecipientName        = dto.RecipientName;
        if (dto.RecipientDesignation is not null) d.RecipientDesignation = dto.RecipientDesignation;
        if (dto.RecipientAddress     is not null) d.RecipientAddress     = dto.RecipientAddress;
        if (dto.RecipientPhone       is not null) d.RecipientPhone       = dto.RecipientPhone;
        if (dto.DeliverBy.HasValue)               d.DeliverBy            = dto.DeliverBy;
        if (dto.ExpectedDeliveryOn.HasValue)      d.ExpectedDeliveryOn   = dto.ExpectedDeliveryOn;
        if (dto.DeliveredOn.HasValue)             d.DeliveredOn          = dto.DeliveredOn;
        if (dto.ReceivedBy           is not null) d.ReceivedBy           = dto.ReceivedBy;
        if (dto.Notes                is not null) d.Notes                = dto.Notes;
        if (dto.Status               is not null) d.Status               = dto.Status;

        if (dto.PodDocumentId.HasValue)
            d.PodDocumentId = await ResolveOrgDocumentAsync(dto.PodDocumentId.Value, orgId, ct);

        // Recording a delivery date IS marking it delivered. Requiring the client to send both
        // is how a row ends up dated but still 'in_transit', which keeps EMD_DELIVERY_OVERDUE
        // nagging about a consignment that already arrived.
        if (dto.DeliveredOn.HasValue && dto.Status is null)
            d.Status = DispatchStatuses.Delivered;

        db.BidActivities.Add(new BidActivity
        {
            BidId   = bidId,
            ActorId = userId,
            Action  = "emd_dispatch_updated",
            ToValue = d.Status,
        });

        await db.SaveChangesAsync(ct);
        return await GetDispatchDtoAsync(d.Id, orgId, ct);
    }

    public async Task DeleteDispatchAsync(Guid orgId, Guid bidId, Guid dispatchId, CancellationToken ct = default)
    {
        var d = await db.BidDispatches
            .FirstOrDefaultAsync(x => x.Id == dispatchId && x.BidId == bidId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Dispatch not found.");

        db.BidDispatches.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    // ── loading ─────────────────────────────────────────────────────────────────

    private async Task<Bid> LoadBidAsync(Guid bidId, Guid orgId, CancellationToken ct)
        => await db.Bids
            .Include(b => b.Tender)
            .FirstOrDefaultAsync(b => b.Id == bidId && b.OrgId == orgId, ct)
           ?? throw new KeyNotFoundException("Bid not found.");

    /// <summary>
    /// The bid's EMD. Newest-first rather than Single: migration 0029's uniqueness index is
    /// GUARDED (it is skipped in any environment that already had duplicate rows), so this must
    /// stay correct without it.
    /// </summary>
    private Task<EmdPayment?> LoadEmdAsync(Guid bidId, Guid orgId, CancellationToken ct)
        => db.EmdPayments
            .Include(e => e.Document)
            .Where(e => e.BidId == bidId && e.OrgId == orgId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<IReadOnlyList<BidDispatchDto>> LoadDispatchesAsync(
        Guid bidId, Guid orgId, CancellationToken ct)
    {
        var rows = await db.BidDispatches
            .Where(d => d.BidId == bidId && d.OrgId == orgId)
            .Include(d => d.Dispatcher)
            .Include(d => d.PodDocument)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(MapDispatch).ToList();
    }

    private async Task<BidDispatchDto> GetDispatchDtoAsync(Guid dispatchId, Guid orgId, CancellationToken ct)
    {
        var d = await db.BidDispatches
            .Include(x => x.Dispatcher)
            .Include(x => x.PodDocument)
            .FirstAsync(x => x.Id == dispatchId && x.OrgId == orgId, ct);
        return MapDispatch(d);
    }

    /// <summary>
    /// Scoping the lookup to the org is what stops a caller attaching another org's document —
    /// an id from outside the org is indistinguishable from one that doesn't exist.
    /// </summary>
    private async Task<Guid> ResolveOrgDocumentAsync(Guid documentId, Guid orgId, CancellationToken ct)
    {
        var exists = await db.Documents.AnyAsync(x => x.Id == documentId && x.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Document not found.");
        return documentId;
    }

    private async Task<Guid> ResolveOrgEmdAsync(Guid emdId, Guid orgId, CancellationToken ct)
    {
        var exists = await db.EmdPayments.AnyAsync(x => x.Id == emdId && x.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("EMD record not found.");
        return emdId;
    }

    // ── mapping ─────────────────────────────────────────────────────────────────

    private static bool IsKnownMode(string mode)
        => EmdModes.Electronic.Contains(mode)
           || EmdModes.Instrument.Contains(mode)
           || mode == EmdModes.Exempt;

    /// <summary>
    /// Did the caller send anything worth persisting? Guards against a bare requirement-only
    /// save creating an empty EMD row.
    /// </summary>
    private static bool HasAnyEmdField(SaveBidEmdDto d)
        => d.Amount.HasValue || d.PaymentDate.HasValue || d.PaymentMode is not null
           || d.TransactionRef is not null || d.BankName is not null || d.Status is not null
           || d.InstrumentNumber is not null || d.InstrumentDate.HasValue || d.ValidUntil.HasValue
           || d.IssuingBranch is not null || d.Favouring is not null || d.DueDate.HasValue
           || d.DocumentId.HasValue || d.Notes is not null;

    private static EmdPaymentDto MapEmd(EmdPayment e)
        => new(e.Id, e.BidId, null, e.TenderId, e.GemTenderId, e.TenderTitle,
            e.Amount, e.PaymentDate, e.PaymentMode, e.TransactionRef, e.BankName,
            e.Status, e.RefundDate, e.RefundAmount, e.RefundRef, e.Notes, e.UpdatedAt,
            e.InstrumentNumber, e.InstrumentDate, e.ValidUntil, e.IssuingBranch, e.Favouring,
            e.DueDate, e.DocumentId, e.Document?.Name,
            EmdModes.RequiresPhysicalDispatch(e.PaymentMode));

    // Projection + the "is it late?" rule live in BidDispatchMapper so the EMD register and this
    // tab cannot disagree about the same consignment.
    private static BidDispatchDto MapDispatch(BidDispatch d)
        => BidDispatchMapper.Map(d, DateOnly.FromDateTime(DateTime.UtcNow));
}
