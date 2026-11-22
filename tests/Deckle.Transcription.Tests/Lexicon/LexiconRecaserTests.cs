using Deckle.Transcription.Lexicon;
using Xunit;

namespace Deckle.Transcription.Tests.Lexicon;

[Trait("Category", "unit")]
public sealed class LexiconRecaserTests
{
    private static string Recase(string text, params string[] terms) => new LexiconRecaser(terms).Apply(text);

    [Fact]
    public void AWordInTheWrongCaseTakesTheCaseOfTheTerm()
    {
        Assert.Equal("j'ouvre Deckle puis WinUI", Recase("j'ouvre deckle puis winui", "Deckle", "WinUI"));
    }

    [Fact]
    public void APhraseIsRecasedAsAWhole()
    {
        Assert.Equal("merci Louis Fifre.", Recase("merci louis fifre.", "Louis Fifre"));
    }

    [Fact]
    public void APartOfALongerWordIsLeftAlone()
    {
        Assert.Equal("les deckles et undeckle", Recase("les deckles et undeckle", "Deckle"));
    }

    [Fact]
    public void ATermWithoutACapitalChangesNothing()
    {
        // An all-lowercase term says nothing about case: it must not lowercase a sentence start.
        Assert.Equal("Onnx tourne.", Recase("Onnx tourne.", "onnx"));
    }

    [Fact]
    public void PunctuationAndApostrophesAreWordBoundaries()
    {
        Assert.Equal("l'Anytype, (Anytype)", Recase("l'anytype, (anytype)", "Anytype"));
    }

    [Fact]
    public void OnlyTheCaseChanges()
    {
        string text = "un deckle-like et deckle";

        string result = Recase(text, "Deckle");

        Assert.Equal(text.Length, result.Length);
        Assert.Equal(text.ToLowerInvariant(), result.ToLowerInvariant());
    }

    [Fact]
    public void AnEmptyLexiconReturnsTheSameText()
    {
        var recaser = new LexiconRecaser([]);
        const string text = "rien à faire";

        Assert.True(recaser.IsEmpty);
        Assert.Same(text, recaser.Apply(text));
    }
}
