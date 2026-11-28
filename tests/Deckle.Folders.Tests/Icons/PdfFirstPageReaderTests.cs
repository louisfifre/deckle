using System.Diagnostics.Tracing;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Folders.Tests;

// A PDF's first page drawn by Windows' own renderer, Windows.Data.Pdf, which
// every Windows 10 and 11 ships: the sample's PDF comes back at the size
// asked, on white, with its coloured blocks; a file Windows cannot read
// answers null and is told once, with no name or path.
[Collection(FoldersProviderCollection.Name)]
[Trait("Category", "integration")]
public sealed class PdfFirstPageReaderTests
{
    [Fact]
    public async Task TheSamplePdfFirstPageComesBackAtTheSizeAskedWithItsColours()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);
        string pdf = Path.Combine(root, SampleFolder.PdfFileName);

        ShellImage? page = await PdfFirstPageReader.ReadAsync(pdf, 160, TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(160, page.Height); // A4 portrait: the height is the longer side
        Assert.Equal(113, page.Width);
        Assert.Equal(page.Width * page.Height * 4, page.Bgra.Length);
        // The top left corner is the page's white; the middle of the lower
        // blue block is blue. B, G, R, A.
        Assert.True(page.Bgra[0] > 240 && page.Bgra[1] > 240 && page.Bgra[2] > 240);
        int x = page.Width / 2, y = (int)(page.Height * (842 - 325) / 842.0);
        int at = (y * page.Width + x) * 4;
        Assert.True(page.Bgra[at] > page.Bgra[at + 2] + 60, $"B={page.Bgra[at]} R={page.Bgra[at + 2]}");
    }

    [Theory]
    [InlineData("zeroes")]
    [InlineData("text")]
    [InlineData("missing")]
    public async Task AFileWindowsCannotReadAsAPdfAnswersNullAndIsToldWithoutItsPath(string kind)
    {
        using var scratch = new ScratchFolder();
        string path = Path.Combine(scratch.Path, "Contrat secret.pdf");
        if (kind == "zeroes") await File.WriteAllBytesAsync(path, new byte[4096], TestContext.Current.CancellationToken);
        if (kind == "text") await File.WriteAllTextAsync(path, "%PDF-1.4 not really", TestContext.Current.CancellationToken);
        using var listener = new TestEventListener("Deckle-Folders");

        ShellImage? page = await PdfFirstPageReader.ReadAsync(path, 160, TestContext.Current.CancellationToken);

        Assert.Null(page);
        EventWrittenEventArgs told = Assert.Single(listener.Events, e => e.EventId == DeckleFoldersSource.EvtPreviewNotDrawn);
        Assert.Equal(EventLevel.Warning, told.Level);
        Assert.Equal("pdf", told.Payload?[0]);
        Assert.DoesNotContain(told.Payload ?? [], value => value is string text && text.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ACancelledRenderStopsWithoutAnAnswer()
    {
        using var scratch = new ScratchFolder();
        string root = SampleFolder.Create(scratch.Path);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PdfFirstPageReader.ReadAsync(Path.Combine(root, SampleFolder.PdfFileName), 160, cancelled.Token));
    }

    [Theory]
    [InlineData(595, 842, 160, 113, 160)]
    [InlineData(842, 595, 160, 160, 113)]
    [InlineData(0, 0, 64, 64, 64)]
    public void APageFitsTheSizeAskedOnItsLongerSide(double width, double height, int size, uint w, uint h)
    {
        Assert.Equal((w, h), PdfFirstPageReader.Fit(width, height, size));
    }
}
