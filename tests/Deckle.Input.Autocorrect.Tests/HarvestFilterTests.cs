using System.IO;
using Deckle.Input.Autocorrect.Cli.Harvest;
using Deckle.Input.Autocorrect.Lexicon;
using Xunit;

namespace Deckle.Input.Autocorrect.Tests;

// What the harvest keeps and what it drops. Signal-only: alphabetic French-shaped
// tokens pass; single characters, digit-bearing tokens, and equal "pairs" are
// noise or sensitive and never reach disk. Unknown-word classification rides on
// the lexicon's own key space (lowercased + NFC), so case never leaks a known
// word through as unknown.
public sealed class HarvestFilterTests
{
    // A tiny French lexicon: two known forms, one accented.
    private static FrequencyLexicon Lexicon() =>
        FrequencyLexicon.LoadTsv(new StringReader("mange\t100\nécole\t50\n"));

    [Theory]
    [InlineData("ça", true)]
    [InlineData("aujourd'hui", true)]                 // interior apostrophe
    [InlineData("peut-être", true)]                   // interior hyphen
    [InlineData("a", false)]                          // single character
    [InlineData("ab12", false)]                       // digit-bearing
    [InlineData("12", false)]                         // pure digits
    [InlineData("c#", false)]                         // symbol
    [InlineData("l'", false)]                         // trailing-apostrophe elision marker
    [InlineData("-mot", false)]                       // leading connector
    [InlineData("anticonstitutionnellement", false)]  // 25 chars — over the length cap
    public void IsHarvestableTokenKeepsOnlyAlphabeticFrenchShapedTokens(string word, bool expected) =>
        Assert.Equal(expected, HarvestFilter.IsHarvestableToken(word));

    [Fact]
    public void ACorrectionPairNeedsBothSidesHarvestableAndDifferent()
    {
        Assert.True(HarvestFilter.IsCorrectionPair("captes", "capte"));
        Assert.False(HarvestFilter.IsCorrectionPair("capte", "capte"));   // identical
        Assert.False(HarvestFilter.IsCorrectionPair("co2", "co"));        // digit-bearing side
    }

    [Fact]
    public void AKnownFormIsNotUnknownRegardlessOfCase()
    {
        var french = Lexicon();
        Assert.False(HarvestFilter.IsUnknownWord("mange", french));
        Assert.False(HarvestFilter.IsUnknownWord("Mange", french));  // lowercased before lookup
        Assert.False(HarvestFilter.IsUnknownWord("école", french));  // accent preserved
    }

    [Fact]
    public void AnAbsentHarvestableFormIsUnknown()
    {
        var french = Lexicon();
        Assert.True(HarvestFilter.IsUnknownWord("captes", french));   // the coverage gap
    }

    [Fact]
    public void NonHarvestableTokensAreNeverReportedUnknown()
    {
        var french = Lexicon();
        Assert.False(HarvestFilter.IsUnknownWord("a", french));     // too short
        Assert.False(HarvestFilter.IsUnknownWord("pin1234", french)); // digit-bearing
    }
}
