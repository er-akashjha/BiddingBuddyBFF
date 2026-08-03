using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Infrastructure.Extensions;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// The Tenders page's "Recently Crawled" sort and its NEW badge both read
/// <see cref="TenderListItemDto.CrawledAt"/>. It must carry the tender's Mongo
/// <c>createdAt</c> — the instant we FIRST saw it, which the upsert preserves across
/// re-scrapes — and not <c>updatedAt</c> or <c>source.scrapedAt</c>, either of which
/// moves on every revisit and would resurface months-old tenders as new.
/// </summary>
public sealed class TenderListCrawledAtTests
{
    private static readonly DateTime FirstSeen = new(2026, 7, 20, 3, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime LastTouched = new(2026, 7, 23, 9, 40, 0, DateTimeKind.Utc);

    private static TenderSearchItemDto Item(string id, string platform = "gem") => new(
        Id:            id,
        Source:        new TenderSourceDto(platform, "GEM/2026/B/123456", null, null),
        Title:         "Supply of desktop computers",
        Summary:       null,
        Category:      null,
        Organization:  null,
        Location:      null,
        Timeline:      null,
        Financial:     null,
        Qualification: null,
        Commercial:    null,
        Compliance:    null,
        Items:         null,
        Ai:            null,
        Status:        null,
        Documents:     null,
        TechnicalSpecifications: null,
        CreatedAt:     FirstSeen,
        UpdatedAt:     LastTouched);

    [Fact]
    public void CrawledAt_IsFirstSeen_NotLastUpdated()
    {
        var dto = new[] { Item(Guid.NewGuid().ToString()) }.ToListDto().Single();

        Assert.Equal(FirstSeen, dto.CrawledAt);
        Assert.NotEqual(LastTouched, dto.CrawledAt);
    }

    [Fact]
    public void CrawledAt_IsCarriedForEverySourcePortal()
    {
        // The portal filter and the freshness sort are used together, so a non-GeM
        // tender losing its timestamp would silently drop out of a filtered view.
        var items = new[]
        {
            Item(Guid.NewGuid().ToString(), "gem"),
            Item(Guid.NewGuid().ToString(), "eprocure"),
            Item(Guid.NewGuid().ToString(), "coalindiatenders"),
        };

        var mapped = items.ToListDto();

        Assert.All(mapped, d => Assert.Equal(FirstSeen, d.CrawledAt));
        Assert.Equal(
            new[] { "gem", "eprocure", "coalindiatenders" },
            mapped.Select(d => d.Platform));
    }
}
