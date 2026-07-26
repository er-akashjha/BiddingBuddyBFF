using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Shared projection for <see cref="BidDispatch"/>. Both the bid's EMD tab and the org-wide EMD
/// register render consignments, and "is this one late?" has to mean the same thing on both —
/// a register that called a consignment late while the bid page called it fine would be worse
/// than either surface alone.
/// </summary>
public static class BidDispatchMapper
{
    /// <summary>
    /// Late means the buyer does not have it and the window has closed: either the tender's
    /// cut-off passed, or the courier blew its own promised date while still in flight. A
    /// delivered consignment is never late, whatever the dates say.
    /// </summary>
    public static bool IsOverdue(BidDispatch d, DateOnly today)
        => d.DeliveredOn is null
           && d.Status != DispatchStatuses.Delivered
           && ((d.DeliverBy is { } by && by < today)
               || (DispatchStatuses.InFlight.Contains(d.Status)
                   && d.ExpectedDeliveryOn is { } exp && exp < today));

    public static BidDispatchDto Map(BidDispatch d, DateOnly today)
    {
        var days = d.DeliverBy.HasValue ? d.DeliverBy.Value.DayNumber - today.DayNumber : (int?)null;

        return new BidDispatchDto(
            Id:                   d.Id,
            BidId:                d.BidId,
            EmdPaymentId:         d.EmdPaymentId,
            Purpose:              d.Purpose,
            Direction:            d.Direction,
            CourierName:          d.CourierName,
            TrackingNumber:       d.TrackingNumber,
            TrackingUrl:          d.TrackingUrl,
            DispatchedOn:         d.DispatchedOn,
            DispatchedBy:         d.DispatchedBy,
            DispatchedByName:     d.Dispatcher?.Name,
            RecipientName:        d.RecipientName,
            RecipientDesignation: d.RecipientDesignation,
            RecipientAddress:     d.RecipientAddress,
            RecipientPhone:       d.RecipientPhone,
            DeliverBy:            d.DeliverBy,
            ExpectedDeliveryOn:   d.ExpectedDeliveryOn,
            DeliveredOn:          d.DeliveredOn,
            ReceivedBy:           d.ReceivedBy,
            Status:               d.Status,
            PodDocumentId:        d.PodDocumentId,
            PodDocumentName:      d.PodDocument?.Name,
            Notes:                d.Notes,
            IsOverdue:            IsOverdue(d, today),
            DaysToDeliverBy:      days,
            CreatedAt:            d.CreatedAt,
            UpdatedAt:            d.UpdatedAt);
    }
}
