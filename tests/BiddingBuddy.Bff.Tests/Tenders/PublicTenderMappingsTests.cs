using BiddingBuddy.Bff.Core.DTOs.Tenders;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// Locks in the public/guest tender contract: intrinsic fields pass through, and
/// the public DTOs must NEVER expose org-specific or AI-scoring fields. The reflection
/// guard fails the build if someone later widens a public DTO with a personalized field.
/// </summary>
public class PublicTenderMappingsTests
{
    // Fields that must never appear on a public DTO served to anonymous guests.
    private static readonly string[] ForbiddenPublicFields =
    [
        "AiScore", "EligibilityScore", "WinProbability", "RiskScore",
        "AiSummary", "AiAnalysis", "OrgSettings", "CorrigendumCount",
        "IsSaved", "IsTracked",
        // The GeM tender reference must never reach anonymous guests — they could
        // use it to fetch the tender directly on gem.gov.in and bypass the product.
        "GemTenderId",
    ];

    private static TenderListItemDto SampleListItem() => new(
        Id:            Guid.NewGuid(),
        GemTenderId:   "GEM/2026/B/123456",
        Title:         "Supply of desktop computers",
        BuyerOrgName:  "Ministry of Electronics & IT",
        State:         "Delhi",
        Category:      "IT & Software",
        TenderValue:   8_500_000m,
        EmdAmount:     170_000m,
        PublishedDate: new DateOnly(2026, 6, 2),
        ClosingDate:   new DateOnly(2026, 6, 30),
        Status:        "open",
        AiScore:       88,
        WinProbability: 64m,
        IsTracked:     true,
        IsSaved:       true);

    private static TenderDetailDto SampleDetail()
    {
        var financial = new TenderFinancialDto(8_500_000m, new TenderEmdDto(true, 170_000m, "SBI"), null, null);
        var timeline  = new TenderTimelineDto(DateTime.UtcNow, null, DateTime.UtcNow.AddDays(28), null, 90, "1 year");
        var srcDoc    = new TenderSourceDocumentDto("doc-1", "tender", "tender.pdf", HasStoredKey: true);

        return new TenderDetailDto(
            Id:               Guid.NewGuid(),
            GemTenderId:      "GEM/2026/B/123456",
            Title:            "Supply of desktop computers",
            Description:      "Supply and installation of 250 desktops.",
            BuyerOrgName:     "Ministry of Electronics & IT",
            BuyerOrgIdGem:    "mongo-id",
            State:            "Delhi",
            City:             "New Delhi",
            Category:         "IT & Software",
            SubCategory:      "Hardware",
            TenderValue:      8_500_000m,
            EmdAmount:        170_000m,
            PublishedDate:    new DateOnly(2026, 6, 2),
            ClosingDate:      new DateOnly(2026, 6, 30),
            DeliveryDays:     90,
            Status:           "open",
            CorrigendumCount: 2,
            AiScore:          88,
            EligibilityScore: 75,
            WinProbability:   64m,
            RiskScore:        30,
            AiSummary:        "Strong match for your IT profile.",
            AiTags:           ["computers", "hardware"],
            CreatedAt:        DateTime.UtcNow,
            UpdatedAt:        DateTime.UtcNow,
            Documents:        Array.Empty<TenderDocumentDto>(),
            OrgSettings:      new OrgTenderSettingsDto(true, true, 90, "note", ["tag"]),
            AiAnalysis:       new AiAnalysisResultDto(Guid.NewGuid(), "gpt", "elig", "risk", "win", "range", null, null, DateTime.UtcNow),
            Financial:        financial,
            Qualification:    null,
            Commercial:       null,
            Compliance:       null,
            Items:            Array.Empty<TenderItemDto>(),
            Ministry:         "Electronics & IT",
            Department:       "NIC",
            Office:           "HQ",
            BuyerName:        "Rajesh Kumar",
            BuyerDesignation: "Procurement Head",
            SourceDocuments:  [srcDoc],
            Timeline:         timeline);
    }

    [Fact]
    public void ListItem_ToPublic_carries_intrinsic_fields()
    {
        var src = SampleListItem();
        var pub = src.ToPublic();

        Assert.Equal(src.Id, pub.Id);
        Assert.Equal(src.Title, pub.Title);
        Assert.Equal(src.BuyerOrgName, pub.BuyerOrgName);
        Assert.Equal(src.State, pub.State);
        Assert.Equal(src.Category, pub.Category);
        Assert.Equal(src.TenderValue, pub.TenderValue);
        Assert.Equal(src.EmdAmount, pub.EmdAmount);
        Assert.Equal(src.PublishedDate, pub.PublishedDate);
        Assert.Equal(src.ClosingDate, pub.ClosingDate);
        Assert.Equal(src.Status, pub.Status);
    }

    [Fact]
    public void Detail_ToPublic_carries_intrinsic_and_structured_fields()
    {
        var src = SampleDetail();
        var pub = src.ToPublic();

        Assert.Equal(src.Id, pub.Id);
        Assert.Equal(src.Title, pub.Title);
        Assert.Equal(src.Description, pub.Description);
        Assert.Equal(src.Ministry, pub.Ministry);
        Assert.Equal(src.BuyerName, pub.BuyerName);
        Assert.Equal(src.AiTags, pub.Tags);            // AI category tags kept as plain tags
        Assert.Same(src.Financial, pub.Financial);     // structured procurement detail passes through
        Assert.Same(src.Timeline, pub.Timeline);
        Assert.Equal(src.SourceDocuments, pub.SourceDocuments);
    }

    [Fact]
    public void Paged_ToPublic_preserves_pagination_metadata()
    {
        var src = new PagedTenderListDto(
            Items:           [SampleListItem(), SampleListItem()],
            TotalCount:      120,
            Page:            2,
            PageSize:        50,
            TotalPages:      3,
            HasNextPage:     true,
            HasPreviousPage: true);

        var pub = src.ToPublic();

        Assert.Equal(2, pub.Items.Count);
        Assert.Equal(120, pub.TotalCount);
        Assert.Equal(2, pub.Page);
        Assert.Equal(50, pub.PageSize);
        Assert.Equal(3, pub.TotalPages);
        Assert.True(pub.HasNextPage);
        Assert.True(pub.HasPreviousPage);
    }

    [Theory]
    [InlineData(typeof(PublicTenderListItemDto))]
    [InlineData(typeof(PublicTenderDetailDto))]
    [InlineData(typeof(PublicPagedTenderListDto))]
    public void Public_dtos_never_expose_personalized_fields(Type publicDtoType)
    {
        var propertyNames = publicDtoType.GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var forbidden in ForbiddenPublicFields)
        {
            Assert.DoesNotContain(forbidden, propertyNames);
        }
    }
}
