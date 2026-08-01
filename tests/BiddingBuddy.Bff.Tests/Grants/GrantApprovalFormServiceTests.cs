using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BiddingBuddy.Bff.Tests.Grants;

/// <summary>
/// The "Approval to Proceed" form generator. It joins the application (Postgres) to its source grant
/// (Mongo) and must: fill the grant fields, 404 when the application is not in the caller's org, and
/// DEGRADE — never throw — when the grant is missing or unlinked.
///
/// <para>NOTE: UseInMemoryDatabase enforces no org/FK constraints, so the org isolation proven here is
/// the service's own <c>WHERE Id = … AND OrgId = …</c> — the one line keeping one org's form out of
/// another's. The token assertions reopen the produced .docx, so they also prove the embedded template's
/// placeholders are actually reachable end-to-end (the run-splitting trap).</para>
/// </summary>
public sealed class GrantApprovalFormServiceTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static GrantApprovalFormService Service(BffDbContext db, IGrantServicesClient grants) =>
        new(db, grants, NullLogger<GrantApprovalFormService>.Instance);

    private static GrantApplication SeedApp(
        BffDbContext db, Guid orgId, string? mongoGrantId,
        string title = "Snapshot Title", string? agency = null, DateOnly? deadline = null,
        decimal? amountRequested = null, decimal? costSharePct = null, string? sourceUrl = null)
    {
        var app = new GrantApplication
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            MongoGrantId = mongoGrantId,
            Title = title,
            AgencyName = agency,
            Deadline = deadline,
            AmountRequested = amountRequested,
            CostSharePct = costSharePct,
            SourceUrl = sourceUrl,
            Stage = "Qualifying",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.GrantApplications.Add(app);
        db.SaveChanges();
        return app;
    }

    private static GrantSearchItemDto Grant(
        string id = "grant-1",
        string? title = "Tribal Health Program",
        string? agency = "Bureau of Indian Affairs",
        string? sourceUrl = "https://grants.gov/opp/123",
        decimal? totalFunding = 2_000_000m,
        decimal? awardFloor = 50_000m,
        decimal? awardCeiling = 500_000m,
        int? expectedAwards = 5,
        bool? costShareRequired = true,
        decimal? costSharePct = 25m,
        DateTime? closeAt = null) =>
        new(
            Id: id,
            Source: new GrantSourceItemDto("grants-gov", "PLAT-1", "OPP-123", sourceUrl),
            Title: title,
            Summary: null,
            Description: null,
            Agency: new GrantAgencyItemDto(agency, "BIA"),
            Category: null,
            AssistanceListingNumbers: null,
            Eligibility: new GrantEligibilityItemDto(null, null, null, null, null, null, costShareRequired, costSharePct),
            Funding: new GrantFundingItemDto("USD", awardCeiling, awardFloor, totalFunding, expectedAwards, null, null),
            Timeline: new GrantTimelineItemDto(null, closeAt ?? new DateTime(2026, 12, 15), null, null, null, null, null, false),
            TribalIntelligence: null,
            Documents: null,
            Ai: null,
            Status: null,
            IsForecast: false,
            CreatedAt: null,
            UpdatedAt: null);

    private static Mock<IGrantServicesClient> ClientReturning(GrantSearchItemDto? grant)
    {
        var mock = new Mock<IGrantServicesClient>();
        mock.Setup(c => c.GetRawGrantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grant);
        return mock;
    }

    private static string DocxText(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document.Body!.InnerText;
    }

    [Fact]
    public async Task Fills_grant_fields_and_leaves_no_residual_tokens()
    {
        using var db = Db(nameof(Fills_grant_fields_and_leaves_no_residual_tokens));
        var app = SeedApp(db, OrgA, mongoGrantId: "grant-1", title: "Snapshot Title", agency: "Snapshot Agency");

        var result = await Service(db, ClientReturning(Grant()).Object).BuildAsync(app.Id, OrgA, CancellationToken.None);
        var text = DocxText(result.Content);

        // Grant detail wins over the application's own snapshot.
        Assert.Contains("Tribal Health Program", text);
        Assert.Contains("Bureau of Indian Affairs", text);
        Assert.Contains("December 15, 2026", text);
        Assert.Contains("$2,000,000", text);                  // total program funding
        Assert.Contains("$50,000", text);                     // award range floor
        Assert.Contains("$500,000", text);                    // award range ceiling
        Assert.Contains("https://grants.gov/opp/123", text);  // submission portal
        Assert.Contains("Yes", text);                         // cost-share required
        Assert.Contains("25%", text);
        Assert.DoesNotContain("{{", text);                    // every placeholder was substituted
        Assert.Contains("Tribal Health Program", result.FileName);
        Assert.EndsWith(".docx", result.FileName);
    }

    [Fact]
    public async Task Wrong_org_is_not_found()
    {
        using var db = Db(nameof(Wrong_org_is_not_found));
        var app = SeedApp(db, OrgA, mongoGrantId: "grant-1");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Service(db, ClientReturning(Grant()).Object).BuildAsync(app.Id, OrgB, CancellationToken.None));
    }

    [Fact]
    public async Task Degrades_to_snapshot_when_grant_upstream_returns_null()
    {
        using var db = Db(nameof(Degrades_to_snapshot_when_grant_upstream_returns_null));
        var app = SeedApp(db, OrgA, mongoGrantId: "archived-grant",
            title: "Manual Snapshot", agency: "Snapshot Agency",
            deadline: new DateOnly(2027, 1, 1), amountRequested: 123_456m, sourceUrl: "https://snap.example");

        var result = await Service(db, ClientReturning(null).Object).BuildAsync(app.Id, OrgA, CancellationToken.None);
        var text = DocxText(result.Content);

        Assert.Contains("Manual Snapshot", text);
        Assert.Contains("Snapshot Agency", text);
        Assert.Contains("January 1, 2027", text);
        Assert.Contains("$123,456", text);            // total budget from the snapshot's amountRequested
        Assert.Contains("https://snap.example", text);
        Assert.DoesNotContain("{{", text);            // grant-only fields blanked, not left as tokens
    }

    [Fact]
    public async Task Does_not_call_upstream_when_no_grant_is_linked()
    {
        using var db = Db(nameof(Does_not_call_upstream_when_no_grant_is_linked));
        var app = SeedApp(db, OrgA, mongoGrantId: null, title: "Manually Started");
        var client = ClientReturning(Grant());

        var result = await Service(db, client.Object).BuildAsync(app.Id, OrgA, CancellationToken.None);
        var text = DocxText(result.Content);

        Assert.Contains("Manually Started", text);
        Assert.DoesNotContain("{{", text);
        client.Verify(c => c.GetRawGrantAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
