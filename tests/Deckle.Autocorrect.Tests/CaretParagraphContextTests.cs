using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class CaretParagraphContextTests
{
    [Fact]
    public void DocumentStartAnchorsTheClosedParagraph()
    {
        CaretParagraphContextResult result = CaretParagraphContext.ExtractClosed(
            "Un paragraphe.\r\n",
            reachedDocumentStart: true);

        Assert.True(result.Available);
        Assert.Equal("Un paragraphe.", result.Text);
        Assert.Equal(CaretParagraphBoundary.DocumentStart, result.Boundary);
    }

    [Fact]
    public void HardReturnAnchorsOnlyTheLastClosedParagraph()
    {
        CaretParagraphContextResult result = CaretParagraphContext.ExtractClosed(
            "Premier paragraphe.\r\nSecond paragraphe.\r\n",
            reachedDocumentStart: false);

        Assert.True(result.Available);
        Assert.Equal("Second paragraphe.", result.Text);
        Assert.Equal(CaretParagraphBoundary.HardReturn, result.Boundary);
    }

    [Fact]
    public void MissingClosingReturnIsRejected()
    {
        CaretParagraphContextResult result = CaretParagraphContext.ExtractClosed(
            "Paragraphe encore ouvert.",
            reachedDocumentStart: true);

        Assert.False(result.Available);
        Assert.Equal(CaretParagraphContextReasons.ClosingReturnNotFound, result.Reason);
    }

    [Fact]
    public void TruncatedParagraphWithoutAPriorBoundaryIsRejected()
    {
        CaretParagraphContextResult result = CaretParagraphContext.ExtractClosed(
            "fragment tronqué.\n",
            reachedDocumentStart: false);

        Assert.False(result.Available);
        Assert.Equal(CaretParagraphContextReasons.BoundaryNotFound, result.Reason);
    }

    [Fact]
    public void EmptyClosedParagraphIsRejected()
    {
        CaretParagraphContextResult result = CaretParagraphContext.ExtractClosed(
            "Avant.\n\n",
            reachedDocumentStart: true);

        Assert.False(result.Available);
        Assert.Equal(CaretParagraphContextReasons.EmptyText, result.Reason);
    }
}
