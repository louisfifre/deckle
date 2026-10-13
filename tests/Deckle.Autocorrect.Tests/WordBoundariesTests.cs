using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// WordBoundaries is the canonical French tokenization, shared by the live tracker
// and the offline tools. The cases pin the parity guarantee: a word splits the
// same way everywhere.
[Trait("Category", "unit")]
public class WordBoundariesTests
{
    [Theory]
    [InlineData('a', true)]
    [InlineData('é', true)]
    [InlineData('ß', true)]   // a letter beyond ASCII, handled by char.IsLetter
    [InlineData('7', true)]
    [InlineData('-', true)]
    [InlineData(' ', false)]
    [InlineData('.', false)]
    [InlineData('\'', false)]
    public void IsWordCharClassifiesLettersDigitsAndHyphen(char c, bool expected) =>
        Assert.Equal(expected, WordBoundaries.IsWordChar(c));

    [Theory]
    [InlineData('\'', true)]  // U+0027
    [InlineData('’', true)]   // U+2019
    [InlineData('`', false)]
    public void IsApostropheCoversBothGlyphs(char c, bool expected) =>
        Assert.Equal(expected, WordBoundaries.IsApostrophe(c));

    [Theory]
    [InlineData("l", true)]
    [InlineData("L", true)]      // case-insensitive
    [InlineData("qu", true)]
    [InlineData("jusqu", true)]
    [InlineData("lorsqu", true)]
    [InlineData("é", false)]     // a lone accented letter is not a prefix
    [InlineData("le", false)]
    [InlineData("xyz", false)]
    public void IsElisionPrefixMatchesTheClosedSet(string token, bool expected) =>
        Assert.Equal(expected, WordBoundaries.IsElisionPrefix(token));

    // The single rule for rebuilding the screen from (word, boundary) pairs. Both
    // apostrophe glyphs render empty (the elision is already attached to the
    // word); any other boundary renders as is.
    [Theory]
    [InlineData('\'', "")]  // U+0027
    [InlineData('’', "")]   // U+2019
    [InlineData(' ', " ")]
    [InlineData('.', ".")]
    [InlineData(',', ",")]
    public void DisplaySeparatorCollapsesApostrophesAndKeepsTheRest(char boundary, string expected) =>
        Assert.Equal(expected, WordBoundaries.DisplaySeparator(boundary));

    [Fact]
    public void SimpleSentenceSplitsOnSpacesAndPunctuation()
    {
        var tokens = WordBoundaries.Tokenize("le chat dort.").ToArray();
        Assert.Equal(new[] { "le", "chat", "dort" }, tokens);
    }

    [Fact]
    public void ElisionPrefixClosesWithAttachedApostrophe()
    {
        var tokens = WordBoundaries.Tokenize("l'école").ToArray();
        Assert.Equal(new[] { "l'", "école" }, tokens);
    }

    [Fact]
    public void NonElisionApostropheJoinsTheToken()
    {
        var tokens = WordBoundaries.Tokenize("aujourd'hui").ToArray();
        Assert.Equal(new[] { "aujourd'hui" }, tokens);
    }

    [Fact]
    public void TypographicApostropheIsNormalizedToAscii()
    {
        var elided = WordBoundaries.Tokenize("l’école").ToArray();
        Assert.Equal(new[] { "l'", "école" }, elided);

        var joined = WordBoundaries.Tokenize("aujourd’hui").ToArray();
        Assert.Equal(new[] { "aujourd'hui" }, joined);
    }

    [Fact]
    public void HyphenatedWordStaysSingleToken()
    {
        Assert.Equal(new[] { "est-ce" }, WordBoundaries.Tokenize("est-ce").ToArray());
        Assert.Equal(new[] { "rendez-vous" }, WordBoundaries.Tokenize("rendez-vous").ToArray());
    }

    [Fact]
    public void LeadingApostropheIsAPlainSeparator()
    {
        Assert.Equal(new[] { "test" }, WordBoundaries.Tokenize("'test").ToArray());
    }

    [Fact]
    public void TrailingUnterminatedTokenIsEmitted()
    {
        Assert.Equal(new[] { "mot" }, WordBoundaries.Tokenize("mot").ToArray());
    }

    [Fact]
    public void ConsecutiveBoundariesProduceNoEmptyTokens()
    {
        var tokens = WordBoundaries.Tokenize("un,  deux").ToArray();
        Assert.Equal(new[] { "un", "deux" }, tokens);
    }

    [Fact]
    public void JusquPrefixClosesElision()
    {
        var tokens = WordBoundaries.Tokenize("jusqu'à").ToArray();
        Assert.Equal(new[] { "jusqu'", "à" }, tokens);
    }
}
