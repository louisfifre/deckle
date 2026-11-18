using Xunit;

namespace Deckle.Paste.Tests;

// The window's list of what was pasted since it opened: newest first, one row
// per saved file, a problem row that carries its reason, emptied on close.
[Trait("Category", "unit")]
public sealed class PasteHistoryTests
{
    private const string Folder = @"C:\Users\Public\Documents\Inbox";

    private static readonly Func<string, bool> NoFolders = _ => false;

    [Fact]
    public void EachPasteAddsItsRowsAboveTheOlderOnesInItsOwnOrder()
    {
        var history = new PasteHistory();

        history.Add([PasteEntry.Image("first.png")]);
        history.Add([PasteEntry.File("a.pdf", PasteFileKind.Document), PasteEntry.File("b.mp4", PasteFileKind.Video)]);

        Assert.Equal(["a.pdf", "b.mp4", "first.png"], history.Entries.Select(e => e.Name));
    }

    [Fact]
    public void ClearingEmptiesTheList()
    {
        var history = new PasteHistory();
        history.Add([PasteEntry.Image("first.png")]);

        history.Clear();

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void TheListKeepsItsNewestRowsUpToItsCapacity()
    {
        var history = new PasteHistory();

        for (int i = 0; i < PasteHistory.Capacity + 5; i++)
            history.Add([PasteEntry.Image($"{i}.png")]);

        Assert.Equal(PasteHistory.Capacity, history.Entries.Count);
        Assert.Equal($"{PasteHistory.Capacity + 4}.png", history.Entries[0].Name);
        Assert.Equal("5.png", history.Entries[^1].Name);
    }

    [Fact]
    public void ASavedImageIsOneImageRowNamedForItsFile()
    {
        var result = new PasteSaveResult(true, [Path.Combine(Folder, "paste-2026-11-18-143005.png")], null);

        PasteEntry row = Assert.Single(PasteHistory.RowsFor(new PastedImage(new byte[] { 1 }), result, NoFolders));

        Assert.Equal(PasteEntryKind.Image, row.Kind);
        Assert.Equal("paste-2026-11-18-143005.png", row.Name);
        Assert.False(row.IsProblem);
    }

    [Fact]
    public void ASavedTextCarriesTheBeginningOfItsWords()
    {
        var result = new PasteSaveResult(true, [Path.Combine(Folder, "note.txt")], null);

        PasteEntry row = Assert.Single(PasteHistory.RowsFor(new PastedText("Buy\r\n  bread"), result, NoFolders));

        Assert.Equal(PasteEntryKind.Text, row.Kind);
        Assert.Equal("Buy bread", row.Detail);
    }

    [Fact]
    public void CopiedFilesAreOneRowEachWithTheKindOfTheirName()
    {
        string album = Path.Combine(Folder, "Album");
        var result = new PasteSaveResult(true, [Path.Combine(Folder, "clip.MP4"), album + Path.DirectorySeparatorChar], null);

        IReadOnlyList<PasteEntry> rows = PasteHistory.RowsFor(
            new PastedFiles(["x", "y"]), result, path => path.TrimEnd(Path.DirectorySeparatorChar) == album);

        Assert.Equal(["clip.MP4", "Album"], rows.Select(r => r.Name));
        Assert.Equal([PasteFileKind.Video, PasteFileKind.Folder], rows.Select(r => r.FileKind));
    }

    [Fact]
    public void AFailedPasteIsOneProblemRowWithItsReason()
    {
        var result = new PasteSaveResult(false, [], PasteSaveErrors.NoFolder);

        PasteEntry row = Assert.Single(PasteHistory.RowsFor(new PastedText("x"), result, NoFolders));

        Assert.Equal(PasteEntryKind.NotSaved, row.Kind);
        Assert.True(row.IsProblem);
        Assert.Equal(PasteSaveErrors.NoFolder, row.Detail);
    }

    [Fact]
    public void ACopyThatStoppedPartWayShowsWhyAboveWhatItLeftInTheFolder()
    {
        var result = new PasteSaveResult(false, [Path.Combine(Folder, "a.pdf")], PasteSaveErrors.SourceMissing);

        IReadOnlyList<PasteEntry> rows = PasteHistory.RowsFor(new PastedFiles(["a", "b"]), result, NoFolders);

        Assert.Equal([PasteEntryKind.NotSaved, PasteEntryKind.File], rows.Select(r => r.Kind));
        Assert.Equal(PasteSaveErrors.SourceMissing, rows[0].Detail);
    }

    [Fact]
    public void AProblemRowRefusesAKindThatIsNoProblem()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasteEntry.Problem(PasteEntryKind.Image, "x"));
    }
}
