using System.IO;
using Deckle.Autocorrect.Lab;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// What the harvest keeps and what it drops. Signal-only: alphabetic French-shaped
// tokens pass; single characters, digit-bearing tokens, and equal "pairs" are
// noise or sensitive and never reach disk. Unknown-word classification rides on
// the lexicon's own key space (lowercased + NFC), so case never leaks a known
// word through as unknown.
[Trait("Category", "unit")]
public sealed class HarvestFilterTests
{
    private static FrequencyLexicon Lexicon() =>
        FrequencyLexicon.LoadTsv(new StringReader("mange\t100\nécole\t50\n"));

    [Theory]
    [InlineData("ça", true)]
    [InlineData("aujourd'hui", true)]                 // interior apostrophe
    [InlineData("peut-être", true)]                   // interior hyphen
    [InlineData("a", false)]
    [InlineData("ab12", false)]
    [InlineData("12", false)]
    [InlineData("c#", false)]
    [InlineData("l'", false)]                       // trailing-apostrophe elision marker
    [InlineData("-mot", false)]                       // leading connector
    public void IsHarvestableTokenKeepsOnlyAlphabeticFrenchShapedTokens(string word, bool expected) =>
        Assert.Equal(expected, HarvestFilter.IsHarvestableToken(word));

    [Fact]
    public void LengthCapIsEnforcedAtItsBoundary()
    {
        Assert.True(HarvestFilter.IsHarvestableToken(new string('a', HarvestFilter.MaxTokenLength)));
        Assert.False(HarvestFilter.IsHarvestableToken(new string('a', HarvestFilter.MaxTokenLength + 1)));
    }

    [Fact]
    public void ACorrectionPairNeedsBothSidesHarvestableAndDifferent()
    {
        Assert.True(HarvestFilter.IsCorrectionPair("captes", "capte"));
        Assert.False(HarvestFilter.IsCorrectionPair("capte", "capte"));
        Assert.False(HarvestFilter.IsCorrectionPair("co2", "co"));
    }

    [Fact]
    public void AKnownFormIsNotUnknownRegardlessOfCase()
    {
        var french = Lexicon();
        Assert.False(HarvestFilter.IsUnknownWord("mange", french));
        Assert.False(HarvestFilter.IsUnknownWord("Mange", french));
        Assert.False(HarvestFilter.IsUnknownWord("école", french));
    }

    [Fact]
    public void AnAbsentHarvestableFormIsUnknown()
    {
        var french = Lexicon();
        Assert.True(HarvestFilter.IsUnknownWord("captes", french));
    }

    [Fact]
    public void NonHarvestableTokensAreNeverReportedUnknown()
    {
        var french = Lexicon();
        Assert.False(HarvestFilter.IsUnknownWord("a", french));
        Assert.False(HarvestFilter.IsUnknownWord("pin1234", french));
    }
}
