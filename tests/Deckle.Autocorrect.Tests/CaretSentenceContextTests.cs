using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class CaretSentenceContextTests
{
    [Fact]
    public void DocumentStartAnchorsTheSentence()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "Cette phrase finit.",
            reachedDocumentStart: true);

        Assert.True(result.Available);
        Assert.Equal("Cette phrase finit.", result.Text);
        Assert.Equal(CaretSentenceBoundary.DocumentStart, result.Boundary);
    }

    [Fact]
    public void HardReturnAnchorsOnlyTheFollowingSentence()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "Titre\r\nCette phrase finit.",
            reachedDocumentStart: false);

        Assert.True(result.Available);
        Assert.Equal("Cette phrase finit.", result.Text);
        Assert.Equal(CaretSentenceBoundary.HardReturn, result.Boundary);
    }

    [Fact]
    public void TerminalPunctuationAndSpaceAnchorTheFollowingSentence()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "Première phrase. Deuxième phrase fautive.",
            reachedDocumentStart: false);

        Assert.True(result.Available);
        Assert.Equal("Deuxième phrase fautive.", result.Text);
        Assert.Equal(CaretSentenceBoundary.TerminalPunctuation, result.Boundary);
    }

    [Fact]
    public void TerminalPunctuationWithoutSpaceDoesNotInventABoundary()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "version1.2 finit.",
            reachedDocumentStart: false);

        Assert.False(result.Available);
        Assert.Equal(CaretSentenceContextReasons.BoundaryNotFound, result.Reason);
    }

    [Fact]
    public void TruncatedTextWithoutAVisibleBoundaryIsRejected()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "fragment dont le début est hors de la lecture.",
            reachedDocumentStart: false);

        Assert.False(result.Available);
        Assert.Equal(CaretSentenceContextReasons.BoundaryNotFound, result.Reason);
    }

    [Fact]
    public void MostRecentProvenBoundaryWins()
    {
        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            "Avant. Milieu ? Dernière phrase.",
            reachedDocumentStart: true);

        Assert.True(result.Available);
        Assert.Equal("Dernière phrase.", result.Text);
        Assert.Equal(CaretSentenceBoundary.TerminalPunctuation, result.Boundary);
    }

    [Fact]
    public void OverlongSentenceIsRejected()
    {
        string sentence = new('a', CaretSentenceContext.MaxSentenceLength + 1);

        CaretSentenceContextResult result = CaretSentenceContext.Extract(
            sentence,
            reachedDocumentStart: true);

        Assert.False(result.Available);
        Assert.Equal(CaretSentenceContextReasons.SentenceTooLong, result.Reason);
    }
}
