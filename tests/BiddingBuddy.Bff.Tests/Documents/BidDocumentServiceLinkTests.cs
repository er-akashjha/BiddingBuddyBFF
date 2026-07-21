using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Documents;

/// <summary>
/// <c>POST /api/bids/{id}/documents</c> is idempotent by contract — re-linking a document is
/// documented as "a no-op that returns the existing row". <see cref="BidDocumentService.LinkAsync"/>
/// delivers that with a check-then-act pair (existence query, then insert) that is not atomic, so a
/// concurrent link of the same (bid, document) turns the second request into a 23505 and a 500.
/// <para>
/// This suite runs on <c>UseInMemoryDatabase</c>, which enforces neither foreign keys nor unique
/// indexes — <c>ux_bid_documents_bid_doc</c> does not exist here and the collision cannot be
/// provoked organically. A test that merely inserted the same pair twice in-memory would pass
/// against the unfixed code and prove nothing. So the race is injected at the only seam that
/// matters: <c>SaveChangesAsync</c> throws the <see cref="DbUpdateException"/>/<see cref="PostgresException"/>
/// pair Npgsql would raise, while the "winner" row lands through a second context on the same
/// store. That exercises the real recovery branch — detach, re-read, adopt.
/// </para>
/// </summary>
public sealed class BidDocumentServiceLinkTests
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static DbContextOptions<BffDbContext> Options(string name) =>
        new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options;

    /// <summary>Postgres' own 23505, shaped the way Npgsql surfaces it through EF Core.</summary>
    private static DbUpdateException UniqueViolation() =>
        new("An error occurred while saving the entity changes. See the inner exception for details.",
            new PostgresException(
                messageText:      "duplicate key value violates unique constraint \"ux_bid_documents_bid_doc\"",
                severity:         "ERROR",
                invariantSeverity:"ERROR",
                sqlState:         PostgresErrorCodes.UniqueViolation,
                tableName:        "bid_documents",
                constraintName:   "ux_bid_documents_bid_doc"));

    /// <summary>The other candidate for the production 500 — a missing relation.</summary>
    private static DbUpdateException UndefinedTable() =>
        new("An error occurred while saving the entity changes. See the inner exception for details.",
            new PostgresException(
                messageText:      "relation \"bid_documents\" does not exist",
                severity:         "ERROR",
                invariantSeverity:"ERROR",
                sqlState:         PostgresErrorCodes.UndefinedTable));

    private static async Task<(Bid bid, Document doc)> SeedAsync(BffDbContext db, Guid orgId = default)
    {
        orgId = orgId == default ? OrgA : orgId;

        var user = new User { Id = User, Email = "linker@example.com", Name = "Linker" };
        var bid  = new Bid { Id = Guid.NewGuid(), OrgId = orgId, Title = "Laptops 2026", CreatedBy = User };
        var doc  = new Document
        {
            Id         = Guid.NewGuid(),
            OrgId      = orgId,
            Name       = "GST certificate",
            FileName   = "gst_cert.pdf",
            S3Key      = $"orgs/{orgId}/docs/{Guid.NewGuid()}/gst_cert.pdf",
            MimeType   = "application/pdf",
            FileSizeKb = 245,
            UploadedBy = User,
        };

        if (!await db.Users.AnyAsync(u => u.Id == User)) db.Users.Add(user);
        db.Bids.Add(bid);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (bid, doc);
    }

    private static BidDocumentService Service(BffDbContext db, CapturingLogger? log = null) =>
        new(db, log ?? new CapturingLogger());

    // ── The happy path (there was no coverage of this at all) ────────────────

    [Fact]
    public async Task Link_FreshDocument_PersistsOneRow()
    {
        using var db = new BffDbContext(Options(nameof(Link_FreshDocument_PersistsOneRow)));
        var (bid, doc) = await SeedAsync(db);

        var result = await Service(db).LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default);

        Assert.Equal(doc.Id, result.DocumentId);
        Assert.Equal("vault", result.Source);
        Assert.Equal("Linker", result.AddedByName);
        Assert.Single(await db.BidDocuments.ToListAsync());
    }

    [Fact]
    public async Task Link_SameDocumentTwiceSequentially_IsANoOp_AndReturnsTheSameRow()
    {
        using var db = new BffDbContext(Options(nameof(Link_SameDocumentTwiceSequentially_IsANoOp_AndReturnsTheSameRow)));
        var (bid, doc) = await SeedAsync(db);
        var svc = Service(db);

        var first  = await svc.LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default);
        var second = await svc.LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default);

        // The pre-check catches this one; the row and its id must be identical.
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.BidDocuments.ToListAsync());
    }

    // ── Tenant isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task Link_AnotherOrgsDocument_Throws_AndLinksNothing()
    {
        using var db = new BffDbContext(Options(nameof(Link_AnotherOrgsDocument_Throws_AndLinksNothing)));
        var (bid, _)      = await SeedAsync(db);
        var (_, foreign)  = await SeedAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service(db).LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(foreign.Id), default));

        Assert.Empty(await db.BidDocuments.ToListAsync());
    }

    [Fact]
    public async Task Link_ToAnotherOrgsBid_Throws_AndLinksNothing()
    {
        using var db = new BffDbContext(Options(nameof(Link_ToAnotherOrgsBid_Throws_AndLinksNothing)));
        var (_, doc)       = await SeedAsync(db);
        var (foreignBid, _) = await SeedAsync(db, OrgB);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service(db).LinkAsync(OrgA, foreignBid.Id, User, new LinkBidDocumentDto(doc.Id), default));

        Assert.Empty(await db.BidDocuments.ToListAsync());
    }

    // ── The race (the production 500) ────────────────────────────────────────

    [Fact]
    public async Task Link_WhenAConcurrentRequestWinsTheInsert_ReturnsTheWinnersRow_InsteadOf500()
    {
        const string store = nameof(Link_WhenAConcurrentRequestWinsTheInsert_ReturnsTheWinnersRow_InsteadOf500);
        var options = Options(store);

        using var seed = new BffDbContext(options);
        var (bid, doc) = await SeedAsync(seed);

        Guid winnerId = default;

        // Our pre-check found nothing; between it and our insert the other request commits.
        using var db = new RaceOnFirstSaveDbContext(options, UniqueViolation, onRace: () =>
        {
            using var other = new BffDbContext(options);
            var winner = new BidDocument
            {
                Id = Guid.NewGuid(), OrgId = OrgA, BidId = bid.Id,
                DocumentId = doc.Id, LinkedBy = User, CreatedAt = DateTime.UtcNow,
            };
            other.BidDocuments.Add(winner);
            other.SaveChanges();
            winnerId = winner.Id;
        });

        var log = new CapturingLogger();
        var result = await Service(db, log).LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default);

        // Adopted the winner rather than throwing, and did not create a second link.
        Assert.Equal(winnerId, result.Id);
        Assert.Equal(doc.Id, result.DocumentId);
        Assert.Single(await new BffDbContext(options).BidDocuments.ToListAsync());

        // The failed insert must not still be pending on the context.
        Assert.DoesNotContain(db.ChangeTracker.Entries<BidDocument>(), e => e.State == EntityState.Added);

        // Task 1's whole point: the SQLSTATE and constraint are in the message text, not buried
        // in an exception render that a log export truncates.
        Assert.Contains(log.Messages, m => m.Contains("23505") && m.Contains("ux_bid_documents_bid_doc"));
    }

    [Fact]
    public async Task Link_WhenTheRelationIsMissing_StillFails_AndNamesTheSqlState()
    {
        const string store = nameof(Link_WhenTheRelationIsMissing_StillFails_AndNamesTheSqlState);
        var options = Options(store);

        using var seed = new BffDbContext(options);
        var (bid, doc) = await SeedAsync(seed);

        using var db = new RaceOnFirstSaveDbContext(options, UndefinedTable);
        var log = new CapturingLogger();

        // 42P01 is a real fault, not a race. Swallowing it would turn a missing migration into a
        // silent success and hide the very thing the logging exists to surface.
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
            Service(db, log).LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default));

        Assert.Equal(PostgresErrorCodes.UndefinedTable, ((PostgresException)ex.InnerException!).SqlState);
        Assert.Contains(log.Messages, m => m.Contains("42P01"));
    }

    [Fact]
    public async Task Link_WhenUniqueViolationButTheWinnerRolledBack_Rethrows()
    {
        const string store = nameof(Link_WhenUniqueViolationButTheWinnerRolledBack_Rethrows);
        var options = Options(store);

        using var seed = new BffDbContext(options);
        var (bid, doc) = await SeedAsync(seed);

        // 23505 with no surviving row — the conflicting transaction rolled back after colliding.
        using var db = new RaceOnFirstSaveDbContext(options, UniqueViolation);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            Service(db).LinkAsync(OrgA, bid.Id, User, new LinkBidDocumentDto(doc.Id), default));

        Assert.Empty(await new BffDbContext(options).BidDocuments.ToListAsync());
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fails the first <c>SaveChangesAsync</c> with a supplied database error, optionally letting a
    /// concurrent writer commit first. Later saves behave normally.
    /// </summary>
    private sealed class RaceOnFirstSaveDbContext(
        DbContextOptions<BffDbContext> options,
        Func<DbUpdateException> failure,
        Action? onRace = null) : BffDbContext(options)
    {
        private bool tripped;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (tripped) return base.SaveChangesAsync(cancellationToken);

            tripped = true;
            onRace?.Invoke();
            throw failure();
        }
    }

    private sealed class CapturingLogger : ILogger<BidDocumentService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, ex));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
