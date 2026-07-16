using BiddingBuddy.Bff.Infrastructure.Services;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Tenders;

/// <summary>
/// SellerKey decides whether an org is found on a GeM award ladder — which decides whether their
/// bid auto-resolves to won/lost and whether competitor analytics counts one company once or four
/// times. GeM prints seller names as free text, so these cases are the real variance.
///
/// ⚠ This algorithm is duplicated in BiddingBuddyServices (Core/Domain/SellerKey.cs), which computes
/// the persisted key at ingest. If these tests are edited, mirror the change there.
/// </summary>
public sealed class SellerKeyTests
{
    [Theory]
    // Casing and punctuation.
    [InlineData("JAGJIT TRADERS", "jagjit traders")]
    [InlineData("Jagjit Traders", "jagjit traders")]
    [InlineData("JAGJIT  TRADERS.", "jagjit traders")]
    // The legal-suffix variants that made identical companies fail to match before.
    [InlineData("ABC Pvt Ltd", "abc")]
    [InlineData("ABC Private Limited", "abc")]
    [InlineData("ABC PVT. LTD.", "abc")]
    [InlineData("M/S ABC Pvt Ltd", "abc")]
    [InlineData("Messrs ABC Limited", "abc")]
    [InlineData("ABC & Co", "abc")]
    [InlineData("ABC Company", "abc")]
    [InlineData("ABC LLP", "abc")]
    [InlineData("ABC Corporation", "abc")]
    public void Normalizes_the_same_company_to_one_key(string raw, string expected)
        => Assert.Equal(expected, SellerKey.For(raw));

    [Fact]
    public void All_legal_form_variants_collapse_together()
    {
        var keys = new[]
        {
            "M/S ABC PVT. LTD.", "ABC Private Limited", "abc pvt ltd", "ABC Ltd.", "ABC",
        }.Select(SellerKey.For).Distinct().ToList();

        Assert.Single(keys);
    }

    [Theory]
    // Business descriptors are NOT legal forms — collapsing these would merge different firms,
    // and (worse) could resolve one org's bid against another company's ladder row.
    [InlineData("ABC Traders", "ABC Enterprises")]
    [InlineData("ABC Industries", "ABC Trading")]
    [InlineData("Bajaj Enterprises", "Bajaj Motors")]
    public void Keeps_genuinely_different_firms_apart(string a, string b)
        => Assert.NotEqual(SellerKey.For(a), SellerKey.For(b));

    [Fact]
    public void Leading_M_S_is_dropped_but_a_real_initial_is_not()
    {
        // "M/S" is the Indian "Messrs" prefix → noise.
        Assert.Equal("abc", SellerKey.For("M/S ABC"));
        // ...but a bare "S" elsewhere is a real initial. Stripping it would corrupt the name.
        Assert.Equal("s k industries", SellerKey.For("S K Industries"));
        Assert.Equal("m s dhoni sports", SellerKey.For("M S Dhoni Sports"));
    }

    [Fact]
    public void A_name_of_only_legal_tokens_is_kept_rather_than_collapsed_to_empty()
    {
        // Otherwise every such seller would share one empty key and merge into a single phantom.
        Assert.Equal("private limited", SellerKey.For("Private Limited"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Blank_input_yields_an_empty_key(string? raw)
        => Assert.Equal(string.Empty, SellerKey.For(raw));
}
