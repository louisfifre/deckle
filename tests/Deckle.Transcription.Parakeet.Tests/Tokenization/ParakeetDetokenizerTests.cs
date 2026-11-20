using Deckle.Transcription.Parakeet.Tokenization;
using Xunit;

namespace Deckle.Transcription.Parakeet.Tests.Tokenization;

[Trait("Category", "unit")]
public sealed class ParakeetDetokenizerTests
{
    // Small invented vocabulary in the vocab.txt shape; the real one is never copied into the repo.
    private static readonly string[] Lines =
    [
        "<unk> 0",
        "<|timestamp|> 1",
        "▁ 2",
        "▁Bon 3",
        "jour 4",
        "▁tout 5",
        "▁le 6",
        "▁monde 7",
        ". 8",
        ", 9",
        "▁< 10",
        "<blk> 11",
    ];

    private static ParakeetVocabulary Vocab() => ParakeetVocabulary.Parse(Lines);

    [Fact]
    public void BlankIdIsTheIdOfTheBlankToken()
    {
        Assert.Equal(11, Vocab().BlankId);
        Assert.Equal(12, Vocab().Count);
    }

    [Fact]
    public void DecodeTurnsWordStartMarkersIntoSpacesAndTrims()
    {
        var detokenizer = new ParakeetDetokenizer(Vocab());

        Assert.Equal("Bonjour tout le monde.", detokenizer.Decode([3, 4, 5, 6, 7, 8]));
    }

    [Fact]
    public void DecodeSkipsBlankAndControlTokens()
    {
        var detokenizer = new ParakeetDetokenizer(Vocab());

        Assert.Equal("Bonjour", detokenizer.Decode([11, 1, 3, 0, 4, 11]));
    }

    [Fact]
    public void DecodeCollapsesRepeatedSpaces()
    {
        var detokenizer = new ParakeetDetokenizer(Vocab());

        Assert.Equal("Bon tout", detokenizer.Decode([2, 3, 2, 2, 5]));
    }

    [Fact]
    public void DecodeIgnoresIdsOutsideTheTable()
    {
        var detokenizer = new ParakeetDetokenizer(Vocab());

        Assert.Equal("Bonjour", detokenizer.Decode([3, 999, 4, -1]));
    }

    [Fact]
    public void DecodeOfNothingIsEmpty()
    {
        Assert.Equal(string.Empty, new ParakeetDetokenizer(Vocab()).Decode([]));
    }

    [Fact]
    public void LoneAngleBracketIsPunctuationNotAControlToken()
    {
        var vocabulary = ParakeetVocabulary.Parse(["< 0", "<blk> 1"]);

        Assert.Equal("<", new ParakeetDetokenizer(vocabulary).Decode([0]));
    }

    [Fact]
    public void PiecesKeepTheIndexOfTheirSourceToken()
    {
        var pieces = new ParakeetDetokenizer(Vocab()).Pieces([11, 3, 11, 4, 8]);

        Assert.Equal([1, 3, 4], pieces.Select(p => p.TokenIndex));
        Assert.Equal([" Bon", "jour", "."], pieces.Select(p => p.Text));
    }

    [Fact]
    public void OnlySentencePunctuationEndsASentence()
    {
        var pieces = new ParakeetDetokenizer(Vocab()).Pieces([7, 9, 8]);

        Assert.Equal([false, false, true], pieces.Select(p => p.EndsSentence));
    }

    [Fact]
    public void NormalizeRendersASliceOfPieces()
    {
        var pieces = new ParakeetDetokenizer(Vocab()).Pieces([3, 4, 5, 8, 6, 7]);

        Assert.Equal("le monde", ParakeetDetokenizer.Normalize(pieces.Skip(4)));
    }

    [Fact]
    public void TokenMayContainASpaceBecauseTheIdIsAfterTheLastOne()
    {
        var vocabulary = ParakeetVocabulary.Parse(["a b 0", "<blk> 1"]);

        Assert.Equal("a b", new ParakeetDetokenizer(vocabulary).Decode([0]));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("token x")]
    [InlineData(" 3")]
    public void MalformedLineIsRejected(string line)
    {
        Assert.Throws<FormatException>(() => ParakeetVocabulary.Parse([line, "<blk> 9"]));
    }

    [Fact]
    public void VocabularyWithoutBlankIsRejected()
    {
        Assert.Throws<FormatException>(() => ParakeetVocabulary.Parse(["a 0"]));
    }

    [Fact]
    public void DuplicateIdIsRejected()
    {
        Assert.Throws<FormatException>(() => ParakeetVocabulary.Parse(["a 0", "<blk> 0"]));
    }

    [Fact]
    public void EmptyVocabularyIsRejected()
    {
        Assert.Throws<FormatException>(() => ParakeetVocabulary.Parse([]));
    }
}
