using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// The Postgres mirror's identity is (platform, gem_tender_id) since migration 0022 —
/// these tests pin the InternalPipelineService behaviour that prevents cross-portal
/// tender-id collisions from overwriting each other's rows.
/// </summary>
public sealed class InternalUpsertPlatformTests
{
    private static InternalPipelineService Service(BffDbContext db) =>
        new(db,
            Mock.Of<IBiddingBuddyServicesClient>(),
            Mock.Of<INotificationPublisher>(),
            Mock.Of<INotificationAudienceResolver>(),
            new ConfigurationBuilder().Build(),
            NullLogger<InternalPipelineService>.Instance);

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static UpsertTenderDto Dto(string gemId, string? platform = null, string title = "T") =>
        new(gemId, null, title, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            Platform: platform);

    [Fact]
    public async Task MissingPlatform_DefaultsToGem()
    {
        using var db = Db(nameof(MissingPlatform_DefaultsToGem));
        await Service(db).UpsertTenderAsync(Dto("GEM/2026/B/1"), CancellationToken.None);

        var row = await db.Tenders.SingleAsync();
        Assert.Equal("gem", row.Platform);
    }

    [Fact]
    public async Task SameId_DifferentPlatform_CreatesTwoRows()
    {
        using var db = Db(nameof(SameId_DifferentPlatform_CreatesTwoRows));
        var svc = Service(db);

        // A hypothetical id collision across portals must NOT overwrite the other row.
        await svc.UpsertTenderAsync(Dto("2026_X_1", "gem", "gem tender"), CancellationToken.None);
        await svc.UpsertTenderAsync(Dto("2026_X_1", "eprocure", "eprocure tender"), CancellationToken.None);

        Assert.Equal(2, await db.Tenders.CountAsync());
        Assert.Equal("gem tender", (await db.Tenders.SingleAsync(t => t.Platform == "gem")).Title);
        Assert.Equal("eprocure tender", (await db.Tenders.SingleAsync(t => t.Platform == "eprocure")).Title);
    }

    [Fact]
    public async Task SamePlatformAndId_Updates()
    {
        using var db = Db(nameof(SamePlatformAndId_Updates));
        var svc = Service(db);

        var first  = await svc.UpsertTenderAsync(Dto("2026_ALHW_915157_1", "eprocure", "v1"), CancellationToken.None);
        var second = await svc.UpsertTenderAsync(Dto("2026_ALHW_915157_1", "EPROCURE", "v2"), CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);                     // case-insensitive: normalized to lowercase
        Assert.Equal(1, await db.Tenders.CountAsync());
        Assert.Equal("v2", (await db.Tenders.SingleAsync()).Title);
    }
}
