using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// Tests sur WordBoundaries — la tokenisation française canonique, partagée par
// le tracker live et le futur entraîneur offline. Les cas reflètent la garantie
// de parité : un mot est découpé de la même façon partout. On vérifie les
// classes de caractères, l'élision (« l' » se ferme, « aujourd'hui » reste
// entier), le trait d'union (« est-ce » entier) et la normalisation de
// l'apostrophe typographique U+2019 vers U+0027.
[Trait("Category", "unit")]
public class WordBoundariesTests
{
    [Theory]
    [InlineData('a', true)]
    [InlineData('é', true)]
    [InlineData('ß', true)]   // lettre non-latine gérée naturellement par char.IsLetter
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
    [InlineData("L", true)]      // insensible à la casse
    [InlineData("qu", true)]
    [InlineData("jusqu", true)]
    [InlineData("lorsqu", true)]
    [InlineData("é", false)]     // accent seul, pas un préfixe
    [InlineData("le", false)]
    [InlineData("xyz", false)]
    public void IsElisionPrefixMatchesTheClosedSet(string token, bool expected) =>
        Assert.Equal(expected, WordBoundaries.IsElisionPrefix(token));

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
        // U+2019 en entrée, U+0027 en sortie — élision comme jointure.
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
        // « jusqu' » est un préfixe d'élision : il se ferme, « à » repart neuf.
        var tokens = WordBoundaries.Tokenize("jusqu'à").ToArray();
        Assert.Equal(new[] { "jusqu'", "à" }, tokens);
    }
}
