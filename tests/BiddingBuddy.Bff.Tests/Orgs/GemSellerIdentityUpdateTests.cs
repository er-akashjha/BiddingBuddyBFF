using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Orgs;

/// <summary>
/// The GeM seller identity is the key that ties an org to its rows on GeM's public award ladders,
/// and it is free text on GeM's side — so a typo means "we never find you", and the fallback to the
/// org name only applies while the field is blank. That makes UNSETTING it a real recovery path,
/// not a nicety.
///
/// Every field on UpdateOrgDto follows a null-means-"not supplied" convention, so null cannot also
/// mean "clear". Blank is the clear signal; these tests pin both halves of that contract, since a
/// dropped clear is silent — the endpoint echoes the old value back and the caller reports success.
/// </summary>
public sealed class GemSellerIdentityUpdateTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static OrganizationService Service(BffDbContext db) =>
        new(db,
            Mock.Of<IUserRepository>(),
            Mock.Of<INotificationPublisher>(),
            new TenderAlertRuleService(db),
            new ConfigurationBuilder().Build(),
            NullLogger<OrganizationService>.Instance);

    /// <summary>Only the GeM pair varies across these tests; everything else stays null = untouched.</summary>
    private static UpdateOrgDto Gem(string? sellerId, string? sellerName) =>
        new(null, null, null, null, null, null, null, null, null, null, null,
            sellerId, sellerName, null, null);

    private static async Task<BffDbContext> SeededAsync(string name, string? sellerName = "JAGJIT TRADERS")
    {
        var db = Db(name);
        db.Organizations.Add(new Organization
        {
            Id = Org, Name = "Acme Supplies", OwnedBy = Owner, IsActive = true,
            GemSellerId = "SELLER-123", GemSellerName = sellerName,
        });
        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(), OrgId = Org, UserId = Owner, Role = "owner", Status = "active",
        });
        await db.SaveChangesAsync();
        return db;
    }

    // ── The clear ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlankSellerName_ClearsIt()
    {
        using var db = await SeededAsync(nameof(BlankSellerName_ClearsIt));

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem(null, ""), default);

        Assert.Null(dto.GemSellerName);
        Assert.Null((await db.Organizations.SingleAsync()).GemSellerName);
    }

    [Fact]
    public async Task WhitespaceSellerName_ClearsIt()
    {
        using var db = await SeededAsync(nameof(WhitespaceSellerName_ClearsIt));

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem(null, "   "), default);

        // Whitespace is a cleared input field, not a name. Storing it would leave the org matching
        // nothing on a ladder while the org-name fallback stayed switched off.
        Assert.Null(dto.GemSellerName);
        Assert.Null((await db.Organizations.SingleAsync()).GemSellerName);
    }

    [Fact]
    public async Task BlankSellerId_ClearsIt()
    {
        using var db = await SeededAsync(nameof(BlankSellerId_ClearsIt));

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem("", null), default);

        Assert.Null(dto.GemSellerId);
        Assert.Null((await db.Organizations.SingleAsync()).GemSellerId);
    }

    // ── The convention the clear must not break ───────────────────────────────

    [Fact]
    public async Task NullSellerName_LeavesItUntouched()
    {
        using var db = await SeededAsync(nameof(NullSellerName_LeavesItUntouched));

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem(null, null), default);

        // null still means "field not supplied" for every key on this DTO — a caller patching only
        // the org's website must not wipe its GeM identity.
        Assert.Equal("JAGJIT TRADERS", dto.GemSellerName);
        Assert.Equal("SELLER-123", (await db.Organizations.SingleAsync()).GemSellerId);
    }

    // ── Setting a value ───────────────────────────────────────────────────────

    [Fact]
    public async Task RealSellerName_IsSaved()
    {
        using var db = await SeededAsync(nameof(RealSellerName_IsSaved), sellerName: null);

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem(null, "BAJAJ ENTERPRISES"), default);

        Assert.Equal("BAJAJ ENTERPRISES", dto.GemSellerName);
        Assert.Equal("BAJAJ ENTERPRISES", (await db.Organizations.SingleAsync()).GemSellerName);
    }

    [Fact]
    public async Task SellerName_IsTrimmed()
    {
        using var db = await SeededAsync(nameof(SellerName_IsTrimmed), sellerName: null);

        var dto = await Service(db).UpdateAsync(Org, Owner, Gem(null, "  BAJAJ ENTERPRISES  "), default);

        // Stray whitespace is invisible in the input but would be a different SellerKey, i.e. the
        // same silent "we never find you" this field's check button exists to prevent.
        Assert.Equal("BAJAJ ENTERPRISES", dto.GemSellerName);
    }

    [Fact]
    public async Task ClearingThenSetting_RoundTrips()
    {
        using var db = await SeededAsync(nameof(ClearingThenSetting_RoundTrips));
        var svc = Service(db);

        await svc.UpdateAsync(Org, Owner, Gem(null, ""), default);
        Assert.Null((await db.Organizations.SingleAsync()).GemSellerName);

        var dto = await svc.UpdateAsync(Org, Owner, Gem(null, "NEW NAME"), default);
        Assert.Equal("NEW NAME", dto.GemSellerName);
    }
}
