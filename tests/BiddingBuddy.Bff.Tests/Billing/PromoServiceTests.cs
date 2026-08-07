using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Billing;

/// <summary>
/// Promo validation and discount math. All of it lives server-side on purpose: a client
/// that computed its own discount would be a price-tampering hole, so these numbers are
/// the only ones that ever reach Razorpay.
/// </summary>
public sealed class PromoServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid OtherOrg = Guid.NewGuid();

    private static BffDbContext Db() =>
        new(new DbContextOptionsBuilder<BffDbContext>()
            .UseInMemoryDatabase($"promo-{Guid.NewGuid()}").Options);

    private static PromoCode Code(
        string code = "SAVE20", string type = "percent", long value = 20,
        bool active = true, bool lifetime = false,
        int? max = null, int redeemed = 0,
        DateTime? validUntil = null, string[]? plans = null) =>
        new()
        {
            Id = Guid.NewGuid(), Code = code, DiscountType = type, DiscountValue = value,
            IsActive = active, IsLifetime = lifetime, MaxRedemptions = max,
            RedeemedCount = redeemed, ValidUntil = validUntil, AppliesToPlans = plans,
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task<(BffDbContext Db, PromoService Svc, PromoCode Promo)> SeededAsync(PromoCode promo)
    {
        var db = Db();
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync();
        return (db, new PromoService(db), promo);
    }

    [Fact]
    public async Task Percent_discount_is_computed_off_the_catalog_price()
    {
        var (_, svc, _) = await SeededAsync(Code(value: 40));

        var result = await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Growth);

        Assert.True(result.Valid);
        Assert.Equal(1_199_900, result.OriginalAmountPaise);
        Assert.Equal(479_960, result.DiscountPaise);          // 40% of ₹11,999
        Assert.Equal(719_940, result.FinalAmountPaise);
    }

    [Fact]
    public async Task Codes_are_matched_case_insensitively()
    {
        var (_, svc, _) = await SeededAsync(Code("FOUNDING40", value: 40, lifetime: true));

        var result = await svc.ValidateAsync(Org, "  founding40 ", PlanCatalog.Growth);

        Assert.True(result.Valid);
        Assert.True(result.IsLifetime);
    }

    [Fact]
    public async Task An_unknown_or_inactive_code_is_rejected_as_INVALID_CODE()
    {
        var (_, svc, _) = await SeededAsync(Code("LAUNCH", active: false));

        Assert.Equal("INVALID_CODE", (await svc.ValidateAsync(Org, "NOPE", PlanCatalog.Growth)).Error);
        Assert.Equal("INVALID_CODE", (await svc.ValidateAsync(Org, "LAUNCH", PlanCatalog.Growth)).Error);
    }

    [Fact]
    public async Task An_expired_code_is_rejected_as_EXPIRED()
    {
        var (_, svc, _) = await SeededAsync(Code(validUntil: DateTime.UtcNow.AddDays(-1)));

        Assert.Equal("EXPIRED", (await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Growth)).Error);
    }

    [Fact]
    public async Task A_code_at_its_redemption_cap_is_rejected_as_EXHAUSTED()
    {
        var (_, svc, _) = await SeededAsync(Code(max: 100, redeemed: 100));

        Assert.Equal("EXHAUSTED", (await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Growth)).Error);
    }

    [Fact]
    public async Task A_plan_restricted_code_is_rejected_for_other_plans()
    {
        var (_, svc, _) = await SeededAsync(Code(plans: [PlanCatalog.Pro]));

        Assert.Equal("NOT_APPLICABLE", (await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Growth)).Error);
        Assert.True((await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Pro)).Valid);
    }

    [Fact]
    public async Task The_free_plan_can_never_carry_a_discount()
    {
        var (_, svc, _) = await SeededAsync(Code());

        Assert.Equal("NOT_APPLICABLE", (await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Free)).Error);
    }

    [Fact]
    public async Task A_code_is_one_use_per_org_but_stays_open_to_others()
    {
        var (db, svc, promo) = await SeededAsync(Code());
        db.PromoRedemptions.Add(new PromoRedemption
        {
            Id = Guid.NewGuid(), PromoId = promo.Id, OrgId = Org, RedeemedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Equal("ALREADY_USED", (await svc.ValidateAsync(Org, "SAVE20", PlanCatalog.Growth)).Error);
        Assert.True((await svc.ValidateAsync(OtherOrg, "SAVE20", PlanCatalog.Growth)).Valid);
    }

    /// <summary>
    /// The founding-member promise is "40% off for life" — at renewal the buyer enters
    /// nothing, and the discount they earned still applies.
    /// </summary>
    [Fact]
    public async Task A_lifetime_redemption_auto_applies_on_the_next_checkout()
    {
        var (db, svc, promo) = await SeededAsync(Code("FOUNDING40", value: 40, lifetime: true));
        db.PromoRedemptions.Add(new PromoRedemption
        {
            Id = Guid.NewGuid(), PromoId = promo.Id, OrgId = Org,
            IsLifetime = true, RedeemedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var applied = await svc.ResolveForCheckoutAsync(Org, code: null, PlanCatalog.Growth, 1_199_900);

        Assert.NotNull(applied);
        Assert.Equal("FOUNDING40", applied!.Code);
        Assert.Equal(479_960, applied.DiscountPaise);
        Assert.NotNull(await svc.GetActiveLifetimeDiscountAsync(Org));
    }

    [Fact]
    public async Task An_org_with_no_lifetime_discount_pays_full_price()
    {
        var (_, svc, _) = await SeededAsync(Code());

        Assert.Null(await svc.ResolveForCheckoutAsync(Org, code: null, PlanCatalog.Growth, 1_199_900));
        Assert.Null(await svc.GetActiveLifetimeDiscountAsync(Org));
    }

    /// <summary>
    /// A code that went stale between validate and checkout must fail loudly — silently
    /// charging full price would take money the buyer did not agree to.
    /// </summary>
    [Fact]
    public async Task An_explicitly_entered_bad_code_fails_the_checkout_rather_than_being_dropped()
    {
        var (_, svc, _) = await SeededAsync(Code(validUntil: DateTime.UtcNow.AddDays(-1)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ResolveForCheckoutAsync(Org, "SAVE20", PlanCatalog.Growth, 1_199_900));
        Assert.Equal("EXPIRED", ex.Message);
    }

    /// <summary>Razorpay rejects orders under ₹1, so a too-large flat discount is clamped, not negative.</summary>
    [Fact]
    public async Task A_flat_discount_larger_than_the_price_still_leaves_a_chargeable_amount()
    {
        var (_, svc, _) = await SeededAsync(Code("BIGFLAT", type: "flat", value: 99_999_999));

        var result = await svc.ValidateAsync(Org, "BIGFLAT", PlanCatalog.Starter);

        Assert.True(result.Valid);
        Assert.Equal(100, result.FinalAmountPaise);           // ₹1 floor
        Assert.Equal(299_800, result.DiscountPaise);
    }
}
