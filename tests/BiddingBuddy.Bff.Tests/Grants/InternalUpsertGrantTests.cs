using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiddingBuddy.Bff.Tests.Grants;

/// <summary>
/// The grant shadow-index ingest. Each invariant below fails SILENTLY if broken: a clobbered
/// MongoGrantId orphans every deep-link already sent, a coalescing miss strips a good record down
/// to its identity on the next seed-only re-scrape, and a blank title renders as an empty row in
/// every list — the tender line shipped 5,190 of those.
///
/// <para>NOTE: <c>UseInMemoryDatabase</c> enforces neither foreign keys, unique indexes nor CHECK
/// constraints. It cannot prove the natural-key uniqueness or the status check constraint hold —
/// those live in migration 0028 and would need Testcontainers/PostgreSQL to exercise. What these
/// pin is the service's own merge logic.</para>
/// </summary>
public sealed class InternalUpsertGrantTests
{
    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static InternalGrantPipelineService Service(BffDbContext db) =>
        new(db, NullLogger<InternalGrantPipelineService>.Instance);

    private static UpsertGrantDto Dto(
        string platform = "grants-gov",
        string platformGrantId = "358123",
        string title = "Tribal Health Program",
        string? mongoGrantId = null,
        decimal? awardCeiling = null,
        string? agencyName = null,
        string? status = null,
        int aiScore = 0,
        string? aiSummary = null,
        string[]? applicantTypesRaw = null,
        string[]? applicantTypeCodes = null) =>
        new(Platform: platform,
            PlatformGrantId: platformGrantId,
            Title: title,
            MongoGrantId: mongoGrantId,
            AgencyName: agencyName,
            AwardCeiling: awardCeiling,
            ApplicantTypesRaw: applicantTypesRaw,
            ApplicantTypeCodes: applicantTypeCodes,
            Status: status,
            AiScore: aiScore,
            AiSummary: aiSummary);

    [Fact]
    public async Task First_upsert_creates_and_reports_created()
    {
        using var db = Db(nameof(First_upsert_creates_and_reports_created));

        var result = await Service(db).UpsertGrantAsync(Dto(), CancellationToken.None);

        Assert.True(result.Created);
        Assert.NotEqual(Guid.Empty, result.GrantId);
        Assert.Single(db.GrantOpportunities);
    }

    [Fact]
    public async Task Second_upsert_of_the_same_key_updates_rather_than_duplicating()
    {
        using var db = Db(nameof(Second_upsert_of_the_same_key_updates_rather_than_duplicating));
        var svc = Service(db);

        var first  = await svc.UpsertGrantAsync(Dto(), CancellationToken.None);
        var second = await svc.UpsertGrantAsync(Dto(title: "Renamed"), CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.GrantId, second.GrantId);
        Assert.Single(db.GrantOpportunities);
    }

    [Fact]
    public async Task The_row_id_is_a_real_UUID_because_notification_entity_id_is_UUID_NOT_NULL()
    {
        using var db = Db(nameof(The_row_id_is_a_real_UUID_because_notification_entity_id_is_UUID_NOT_NULL));

        var result = await Service(db).UpsertGrantAsync(Dto(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.GrantId);
    }

    [Fact]
    public async Task Same_grant_id_on_a_different_platform_creates_two_rows()
    {
        // A grant id is only unique within its portal. Keying on the id alone would merge two
        // unrelated opportunities — the mistake migration 0022 had to undo for tenders.
        using var db = Db(nameof(Same_grant_id_on_a_different_platform_creates_two_rows));
        var svc = Service(db);

        await svc.UpsertGrantAsync(Dto(platform: "grants-gov", platformGrantId: "1"), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(platform: "bia", platformGrantId: "1"), CancellationToken.None);

        Assert.Equal(2, db.GrantOpportunities.Count());
    }

    [Fact]
    public async Task Platform_is_normalised_so_casing_cannot_split_one_grant_into_two_rows()
    {
        using var db = Db(nameof(Platform_is_normalised_so_casing_cannot_split_one_grant_into_two_rows));
        var svc = Service(db);

        await svc.UpsertGrantAsync(Dto(platform: "Grants-Gov"), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(platform: "grants-gov"), CancellationToken.None);

        Assert.Single(db.GrantOpportunities);
        Assert.Equal("grants-gov", db.GrantOpportunities.Single().Platform);
    }

    [Fact]
    public async Task Missing_platform_defaults_to_grants_gov()
    {
        using var db = Db(nameof(Missing_platform_defaults_to_grants_gov));

        await Service(db).UpsertGrantAsync(Dto(platform: ""), CancellationToken.None);

        Assert.Equal("grants-gov", db.GrantOpportunities.Single().Platform);
    }

    [Fact]
    public async Task MongoGrantId_is_set_once_and_never_clobbered()
    {
        // Deep-links and notification_reminders rows already point at this id. Changing it orphans
        // every one of them, silently.
        using var db = Db(nameof(MongoGrantId_is_set_once_and_never_clobbered));
        var svc = Service(db);

        await svc.UpsertGrantAsync(Dto(mongoGrantId: "11111111-1111-1111-1111-111111111111"), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(mongoGrantId: "22222222-2222-2222-2222-222222222222"), CancellationToken.None);

        Assert.Equal("11111111-1111-1111-1111-111111111111", db.GrantOpportunities.Single().MongoGrantId);
    }

    [Fact]
    public async Task MongoGrantId_can_still_be_filled_in_later_when_the_first_run_had_none()
    {
        using var db = Db(nameof(MongoGrantId_can_still_be_filled_in_later_when_the_first_run_had_none));
        var svc = Service(db);

        await svc.UpsertGrantAsync(Dto(mongoGrantId: null), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(mongoGrantId: "33333333-3333-3333-3333-333333333333"), CancellationToken.None);

        Assert.Equal("33333333-3333-3333-3333-333333333333", db.GrantOpportunities.Single().MongoGrantId);
    }

    [Fact]
    public async Task A_seed_only_rescrape_does_not_blank_fields_the_first_run_populated()
    {
        // AI failure is routine and survivable (the seed floor is the whole point), so a re-scrape
        // legitimately arrives with most fields null. Overwriting with those nulls would strip a
        // good record down to its identity every time an AI call failed.
        using var db = Db(nameof(A_seed_only_rescrape_does_not_blank_fields_the_first_run_populated));
        var svc = Service(db);

        await svc.UpsertGrantAsync(
            Dto(awardCeiling: 250_000m, agencyName: "Bureau of Indian Affairs", aiScore: 82,
                aiSummary: "Strong fit."),
            CancellationToken.None);

        await svc.UpsertGrantAsync(Dto(), CancellationToken.None);   // seed-only: nulls and score 0

        var row = db.GrantOpportunities.Single();
        Assert.Equal(250_000m, row.AwardCeiling);
        Assert.Equal("Bureau of Indian Affairs", row.AgencyName);
        Assert.Equal(82, row.AiScore);
        Assert.Equal("Strong fit.", row.AiSummary);
    }

    [Fact]
    public async Task A_richer_rescrape_does_overwrite()
    {
        using var db = Db(nameof(A_richer_rescrape_does_overwrite));
        var svc = Service(db);

        await svc.UpsertGrantAsync(Dto(), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(awardCeiling: 500_000m, aiScore: 90), CancellationToken.None);

        var row = db.GrantOpportunities.Single();
        Assert.Equal(500_000m, row.AwardCeiling);
        Assert.Equal(90, row.AiScore);
    }

    [Fact]
    public async Task Applicant_types_are_stored_verbatim()
    {
        using var db = Db(nameof(Applicant_types_are_stored_verbatim));
        const string label = "Native American tribal governments (Federally recognized)";

        await Service(db).UpsertGrantAsync(
            Dto(applicantTypesRaw: [label], applicantTypeCodes: ["07"]), CancellationToken.None);

        var row = db.GrantOpportunities.Single();
        Assert.Equal(label, row.ApplicantTypesRaw!.Single());
        Assert.Equal("07", row.ApplicantTypeCodes!.Single());
    }

    [Fact]
    public async Task An_empty_applicant_type_array_does_not_erase_a_populated_one()
    {
        using var db = Db(nameof(An_empty_applicant_type_array_does_not_erase_a_populated_one));
        var svc = Service(db);

        await svc.UpsertGrantAsync(
            Dto(applicantTypesRaw: ["Small businesses"], applicantTypeCodes: ["23"]), CancellationToken.None);
        await svc.UpsertGrantAsync(Dto(applicantTypesRaw: [], applicantTypeCodes: []), CancellationToken.None);

        Assert.Equal("Small businesses", db.GrantOpportunities.Single().ApplicantTypesRaw!.Single());
    }

    [Fact]
    public async Task A_blank_title_falls_back_rather_than_rendering_as_an_empty_row()
    {
        using var db = Db(nameof(A_blank_title_falls_back_rather_than_rendering_as_an_empty_row));

        await Service(db).UpsertGrantAsync(Dto(title: "   "), CancellationToken.None);

        Assert.Equal("358123", db.GrantOpportunities.Single().Title);
    }

    [Fact]
    public async Task An_unknown_status_becomes_posted_rather_than_violating_the_check_constraint()
    {
        // ck_grant_opportunities_status would 500 the ingest. The shadow index degrades; it does
        // not block the pipeline.
        using var db = Db(nameof(An_unknown_status_becomes_posted_rather_than_violating_the_check_constraint));

        await Service(db).UpsertGrantAsync(Dto(status: "banana"), CancellationToken.None);

        Assert.Equal("posted", db.GrantOpportunities.Single().Status);
    }

    [Theory]
    [InlineData("forecasted")]
    [InlineData("closed")]
    [InlineData("archived")]
    [InlineData("posted")]
    public async Task Valid_statuses_survive(string status)
    {
        using var db = Db($"{nameof(Valid_statuses_survive)}_{status}");

        await Service(db).UpsertGrantAsync(Dto(status: status), CancellationToken.None);

        Assert.Equal(status, db.GrantOpportunities.Single().Status);
    }

    [Fact]
    public async Task A_new_grant_starts_unscanned_so_the_alert_scan_will_pick_it_up()
    {
        using var db = Db(nameof(A_new_grant_starts_unscanned_so_the_alert_scan_will_pick_it_up));

        await Service(db).UpsertGrantAsync(Dto(), CancellationToken.None);

        Assert.Null(db.GrantOpportunities.Single().AlertsScannedAt);
    }
}
