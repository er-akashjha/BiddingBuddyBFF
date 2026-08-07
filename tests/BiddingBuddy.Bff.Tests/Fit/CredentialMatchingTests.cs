using BiddingBuddy.Bff.Core.Fit;

namespace BiddingBuddy.Bff.Tests.Fit;

/// <summary>
/// The matcher sits between "the tender asks for X" and "we hold Y", and both directions of
/// error are expensive: a false negative reports a certificate the org holds as MISSING, which
/// the engine treats as a blocker and pushes the customer off a winnable bid; a false positive
/// silently satisfies a requirement they can't actually meet, which loses them the EMD.
/// </summary>
public class CredentialMatchingTests
{
    [Theory]
    // The edition year is not part of the identity — a current ISO 9001:2015 satisfies a tender
    // still asking for ISO 9001:2008, and treating the year as identity would flag every
    // up-to-date certificate in the country as the wrong one.
    [InlineData("ISO 9001:2015", "ISO 9001")]
    [InlineData("ISO-9001", "ISO 9001")]
    [InlineData("valid ISO 9001 QMS certificate", "ISO 9001")]
    [InlineData("ISO 27001:2022 certification", "ISO 27001")]
    [InlineData("Udyam registration certificate", "UDYAM")]
    [InlineData("MSME certificate", "UDYAM")]
    [InlineData("OEM Authorisation Letter", "OEM")]
    [InlineData("OEM Authorization Certificate", "OEM")]
    public void Normalise_collapses_wording_to_a_comparable_token(string raw, string expected) =>
        Assert.Equal(expected, CredentialMatching.Normalise(raw));

    [Theory]
    [InlineData("ISO 9001:2015", "ISO 9001:2008 certification")]
    [InlineData("ISO 9001", "valid ISO-9001 certificate required")]
    [InlineData("UDYAM", "Udyam registration certificate")]
    public void A_held_credential_satisfies_a_differently_worded_requirement(
        string storedCode, string requirement) =>
        Assert.True(CredentialMatching.Matches(storedCode, CredentialMatching.Normalise(requirement)));

    [Theory]
    // Loose about wording, strict about the number. This is the property that stops the matcher
    // quietly satisfying a security-management requirement with a quality-management certificate.
    [InlineData("ISO 27001:2022", "ISO 9001:2015")]
    [InlineData("ISO 9001", "ISO 14001")]
    [InlineData("UDYAM", "ISO 9001")]
    [InlineData("GST", "OEM authorisation letter")]
    public void A_different_credential_never_satisfies_the_requirement(
        string storedCode, string requirement) =>
        Assert.False(CredentialMatching.Matches(storedCode, CredentialMatching.Normalise(requirement)));

    [Theory]
    [InlineData("", "ISO 9001")]
    [InlineData("ISO 9001", "")]
    [InlineData("   ", "ISO 9001")]
    public void Blank_input_never_matches(string storedCode, string requirement) =>
        Assert.False(CredentialMatching.Matches(storedCode, CredentialMatching.Normalise(requirement)));

    [Fact]
    public void A_short_code_cannot_sweep_up_unrelated_requirements()
    {
        // Containment is length-guarded. Without the guard a two-character stored code would
        // satisfy any requirement whose normalised text happened to contain those characters.
        Assert.False(CredentialMatching.Matches("AB", CredentialMatching.Normalise("ABC certification")));
    }
}
