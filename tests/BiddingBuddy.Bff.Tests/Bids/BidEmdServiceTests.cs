using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Bids;

/// <summary>
/// <see cref="BidEmdService"/> — the EMD record and its courier legs.
/// <para>
/// ⚠ These run on <c>UseInMemoryDatabase</c>, which enforces neither foreign keys nor unique
/// indexes. That means this suite CANNOT catch the failure mode this feature is most exposed
/// to: an INSERT ordered before the row it references, which Postgres rejects with 23503 and
/// the in-memory provider accepts silently. The service links new rows by navigation property
/// (<c>Bid = bid</c>, never <c>BidId = bid.Id</c>) precisely because of that, and the only real
/// verification is a run against Postgres. Nor does <c>ux_emd_payments_bid</c> exist here — the
/// "newest row wins" test below therefore proves the fallback works, not that the index does.
/// </para>
/// </summary>
public sealed class BidEmdServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid OtherOrg = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static BffDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static Bid SeedBid(BffDbContext db, Guid orgId, DateOnly? due = null, string requirement = "unknown")
    {
        var bid = new Bid
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            Title = "Supply of laptops",
            CreatedBy = User,
            DueDate = due,
            EmdRequirement = requirement,
        };
        db.Bids.Add(bid);
        db.SaveChanges();
        return bid;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ── requirement + record creation ───────────────────────────────────────────

    [Fact]
    public async Task Save_requirement_only_does_not_create_an_empty_money_row()
    {
        // Answering "does this tender want EMD?" must not put a ₹0 EMD in the finance register
        // — that would arm EMD_STUCK against something nobody ever paid.
        using var db = NewDb(nameof(Save_requirement_only_does_not_create_an_empty_money_row));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var result = await svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(Requirement: "required"));

        Assert.Equal("required", result.Requirement);
        Assert.Null(result.Emd);
        Assert.Empty(db.EmdPayments);
    }

    [Fact]
    public async Task Save_exempt_records_the_basis_and_still_creates_no_money_row()
    {
        using var db = NewDb(nameof(Save_exempt_records_the_basis_and_still_creates_no_money_row));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var result = await svc.SaveAsync(Org, bid.Id, User,
            new SaveBidEmdDto(Requirement: "exempt", ExemptionBasis: "MSME", Amount: 50_000m));

        Assert.Equal("exempt", result.Requirement);
        Assert.Equal("MSME", result.ExemptionBasis);
        Assert.Null(result.Emd);
        Assert.Empty(db.EmdPayments);
    }

    [Fact]
    public async Task Save_with_instrument_details_creates_the_record_and_flags_physical_dispatch()
    {
        using var db = NewDb(nameof(Save_with_instrument_details_creates_the_record_and_flags_physical_dispatch));
        var bid = SeedBid(db, Org, due: Today.AddDays(10));
        var svc = new BidEmdService(db);

        var result = await svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(
            Requirement: "required",
            Amount: 90_000m,
            PaymentMode: "bg",
            InstrumentNumber: "BG/2026/00417",
            ValidUntil: Today.AddDays(120),
            BankName: "SBI",
            IssuingBranch: "Nariman Point"));

        Assert.NotNull(result.Emd);
        Assert.Equal(90_000m, result.Emd!.Amount);
        Assert.Equal("BG/2026/00417", result.Emd.InstrumentNumber);
        // The whole point of the derived flag: clients shouldn't hard-code the instrument list.
        Assert.True(result.Emd.RequiresPhysicalDispatch);
        // Unstated EMD due date falls back to the bid's own deadline, or it stays unalertable.
        Assert.Equal(Today.AddDays(10), result.Emd.DueDate);
    }

    [Fact]
    public async Task Save_online_mode_does_not_flag_physical_dispatch()
    {
        using var db = NewDb(nameof(Save_online_mode_does_not_flag_physical_dispatch));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var result = await svc.SaveAsync(Org, bid.Id, User,
            new SaveBidEmdDto(Amount: 25_000m, PaymentMode: "neft", TransactionRef: "N123"));

        Assert.NotNull(result.Emd);
        Assert.False(result.Emd!.RequiresPhysicalDispatch);
    }

    [Fact]
    public async Task Save_is_an_upsert_second_call_patches_rather_than_duplicating()
    {
        using var db = NewDb(nameof(Save_is_an_upsert_second_call_patches_rather_than_duplicating));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        await svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(Amount: 90_000m, PaymentMode: "dd"));
        var second = await svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(InstrumentNumber: "DD-9911"));

        Assert.Single(db.EmdPayments);
        Assert.Equal(90_000m, second.Emd!.Amount);        // untouched by the second call
        Assert.Equal("DD-9911", second.Emd.InstrumentNumber);
    }

    [Fact]
    public async Task Save_rejects_an_unknown_payment_mode()
    {
        using var db = NewDb(nameof(Save_rejects_an_unknown_payment_mode));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(PaymentMode: "bitcoin")));
    }

    [Fact]
    public async Task Save_rejects_an_unknown_requirement()
    {
        using var db = NewDb(nameof(Save_rejects_an_unknown_requirement));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(Requirement: "maybe")));
    }

    [Fact]
    public async Task Another_orgs_bid_is_not_found()
    {
        using var db = NewDb(nameof(Another_orgs_bid_is_not_found));
        var bid = SeedBid(db, OtherOrg);
        var svc = new BidEmdService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.GetAsync(Org, bid.Id));
    }

    [Fact]
    public async Task Get_returns_the_newest_emd_when_the_guarded_unique_index_is_absent()
    {
        // Migration 0029 skips ux_emd_payments_bid in any environment that already had
        // duplicates, so the service must not depend on it.
        using var db = NewDb(nameof(Get_returns_the_newest_emd_when_the_guarded_unique_index_is_absent));
        var bid = SeedBid(db, Org);
        db.EmdPayments.AddRange(
            new EmdPayment { OrgId = Org, BidId = bid.Id, Amount = 10m, PaymentDate = Today, CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new EmdPayment { OrgId = Org, BidId = bid.Id, Amount = 20m, PaymentDate = Today, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();

        var result = await new BidEmdService(db).GetAsync(Org, bid.Id);

        Assert.Equal(20m, result.Emd!.Amount);
    }

    // ── dispatches ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_dispatch_links_the_bids_emd_and_inherits_the_bid_deadline()
    {
        using var db = NewDb(nameof(Creating_a_dispatch_links_the_bids_emd_and_inherits_the_bid_deadline));
        var bid = SeedBid(db, Org, due: Today.AddDays(6));
        var svc = new BidEmdService(db);
        var saved = await svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(Amount: 90_000m, PaymentMode: "dd"));

        var d = await svc.CreateDispatchAsync(Org, bid.Id, User,
            new CreateBidDispatchDto(CourierName: "Blue Dart", TrackingNumber: "BD123456789"));

        Assert.Equal(saved.Emd!.Id, d.EmdPaymentId);
        Assert.Equal(Today.AddDays(6), d.DeliverBy);
        Assert.Equal(User, d.DispatchedBy);
    }

    [Fact]
    public async Task A_dispatch_with_a_dispatch_date_is_not_left_as_draft()
    {
        // A draft row is invisible to the "courier is late" scan. Something already handed to
        // the courier must not sit in a state that silences the alert.
        using var db = NewDb(nameof(A_dispatch_with_a_dispatch_date_is_not_left_as_draft));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var d = await svc.CreateDispatchAsync(Org, bid.Id, User,
            new CreateBidDispatchDto(CourierName: "DTDC", DispatchedOn: Today));

        Assert.Equal(DispatchStatuses.Dispatched, d.Status);
    }

    [Fact]
    public async Task A_dispatch_without_a_dispatch_date_stays_draft()
    {
        using var db = NewDb(nameof(A_dispatch_without_a_dispatch_date_stays_draft));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var d = await svc.CreateDispatchAsync(Org, bid.Id, User, new CreateBidDispatchDto(CourierName: "DTDC"));

        Assert.Equal(DispatchStatuses.Draft, d.Status);
    }

    [Fact]
    public async Task Recording_a_delivery_date_also_marks_it_delivered()
    {
        using var db = NewDb(nameof(Recording_a_delivery_date_also_marks_it_delivered));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);
        var d = await svc.CreateDispatchAsync(Org, bid.Id, User,
            new CreateBidDispatchDto(CourierName: "Speed Post", DispatchedOn: Today.AddDays(-3)));

        var updated = await svc.UpdateDispatchAsync(Org, bid.Id, d.Id, User,
            new UpdateBidDispatchDto(DeliveredOn: Today, ReceivedBy: "R. Kumar"));

        Assert.Equal(DispatchStatuses.Delivered, updated.Status);
        Assert.False(updated.IsOverdue);
    }

    [Fact]
    public async Task A_consignment_past_its_cutoff_is_overdue()
    {
        using var db = NewDb(nameof(A_consignment_past_its_cutoff_is_overdue));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var d = await svc.CreateDispatchAsync(Org, bid.Id, User, new CreateBidDispatchDto(
            CourierName: "Blue Dart",
            DispatchedOn: Today.AddDays(-6),
            DeliverBy: Today.AddDays(-1)));

        Assert.True(d.IsOverdue);
        Assert.Equal(-1, d.DaysToDeliverBy);
    }

    [Fact]
    public async Task A_delivered_consignment_is_never_overdue_even_past_its_cutoff()
    {
        using var db = NewDb(nameof(A_delivered_consignment_is_never_overdue_even_past_its_cutoff));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);
        var d = await svc.CreateDispatchAsync(Org, bid.Id, User, new CreateBidDispatchDto(
            CourierName: "Blue Dart", DispatchedOn: Today.AddDays(-9), DeliverBy: Today.AddDays(-2)));

        var updated = await svc.UpdateDispatchAsync(Org, bid.Id, d.Id, User,
            new UpdateBidDispatchDto(DeliveredOn: Today.AddDays(-3)));

        Assert.False(updated.IsOverdue);
    }

    [Fact]
    public async Task A_courier_past_its_promised_date_is_overdue_even_before_the_cutoff()
    {
        using var db = NewDb(nameof(A_courier_past_its_promised_date_is_overdue_even_before_the_cutoff));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        var d = await svc.CreateDispatchAsync(Org, bid.Id, User, new CreateBidDispatchDto(
            CourierName: "DTDC",
            DispatchedOn: Today.AddDays(-5),
            ExpectedDeliveryOn: Today.AddDays(-2),
            DeliverBy: Today.AddDays(4),
            Status: DispatchStatuses.InTransit));

        Assert.True(d.IsOverdue);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_status()
    {
        using var db = NewDb(nameof(Create_rejects_an_unknown_status));
        var bid = SeedBid(db, Org);
        var svc = new BidEmdService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CreateDispatchAsync(Org, bid.Id, User, new CreateBidDispatchDto(Status: "teleported")));
    }

    [Fact]
    public async Task Another_orgs_dispatch_cannot_be_updated_or_deleted()
    {
        using var db = NewDb(nameof(Another_orgs_dispatch_cannot_be_updated_or_deleted));
        var mine = SeedBid(db, Org);
        var theirs = SeedBid(db, OtherOrg);
        var svc = new BidEmdService(db);
        var d = await svc.CreateDispatchAsync(OtherOrg, theirs.Id, User, new CreateBidDispatchDto(CourierName: "X"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.UpdateDispatchAsync(Org, mine.Id, d.Id, User, new UpdateBidDispatchDto(Status: "delivered")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.DeleteDispatchAsync(Org, mine.Id, d.Id));
    }

    [Fact]
    public async Task Attaching_another_orgs_document_is_rejected()
    {
        using var db = NewDb(nameof(Attaching_another_orgs_document_is_rejected));
        var bid = SeedBid(db, Org);
        var foreignDoc = new Document
        {
            Id = Guid.NewGuid(), OrgId = OtherOrg, Name = "Their DD", FileName = "dd.pdf",
            S3Key = $"orgs/{OtherOrg}/docs/{Guid.NewGuid()}/dd.pdf", UploadedBy = User,
        };
        db.Documents.Add(foreignDoc);
        db.SaveChanges();
        var svc = new BidEmdService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SaveAsync(Org, bid.Id, User, new SaveBidEmdDto(Amount: 1m, DocumentId: foreignDoc.Id)));
    }

    // ── mode helpers ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dd", true)]
    [InlineData("bg", true)]
    [InlineData("fdr", true)]
    [InlineData("surety_bond", true)]
    [InlineData("neft", false)]
    [InlineData("online", false)]
    [InlineData("exempt", false)]
    [InlineData(null, false)]
    public void RequiresPhysicalDispatch_is_true_only_for_instruments(string? mode, bool expected)
        => Assert.Equal(expected, EmdModes.RequiresPhysicalDispatch(mode));

    [Theory]
    [InlineData("bg", true)]
    [InlineData("fdr", true)]
    [InlineData("dd", false)]      // a DD is handed over once; it is not extended mid-bid
    [InlineData("neft", false)]
    public void CanExpire_is_true_only_for_instruments_that_get_extended(string mode, bool expected)
        => Assert.Equal(expected, EmdModes.CanExpire(mode));
}
