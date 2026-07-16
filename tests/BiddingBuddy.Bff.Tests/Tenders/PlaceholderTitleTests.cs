using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Services;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// A tender reaches Postgres with its bid number in <c>title</c> when enrichment produced no title
/// (BidProcessor's BffTenderClient falls back to the gem id on the seed-only path). Such a tender has
/// no title, description, or documents, so award mail about it is suppressed — these tests pin which
/// titles count as placeholders, since a false positive silently drops a real notification.
/// </summary>
public sealed class PlaceholderTitleTests
{
    private static Tender T(string title, string gemId = "GEM/2026/R/694493") =>
        new() { Title = title, GemTenderId = gemId };

    [Fact]
    public void TitleEqualToBidNumber_IsPlaceholder() =>
        Assert.True(InternalPipelineService.HasPlaceholderTitle(T("GEM/2026/R/694493")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTitle_IsPlaceholder(string title) =>
        Assert.True(InternalPipelineService.HasPlaceholderTitle(T(title)));

    [Fact]
    public void TitleEqualToBidNumber_IgnoringCaseAndPadding_IsPlaceholder() =>
        Assert.True(InternalPipelineService.HasPlaceholderTitle(T("  gem/2026/r/694493 ")));

    [Fact]
    public void RealTitle_IsNotPlaceholder() =>
        Assert.False(InternalPipelineService.HasPlaceholderTitle(T("Supply of Servo Amplifier Modules")));

    /// <summary>
    /// GeM buyers routinely put the bid number INSIDE a real title. Only an exact match is the
    /// pipeline's fallback — a title that merely contains the bid number is a genuine title and must
    /// still be notified.
    /// </summary>
    [Fact]
    public void TitleContainingBidNumber_IsNotPlaceholder() =>
        Assert.False(InternalPipelineService.HasPlaceholderTitle(
            T("GEM/2026/R/694493 — Supply of Laptops")));
}
