using System.Text;
using Xunit;

namespace Deckle.Paste.Tests;

// A paste becomes files in the chosen folder, never overwrites one, and a
// paste that cannot be saved says why instead of throwing. The saver emits on
// the provider singleton, so the suites that call it share one collection.
[Collection("paste provider singleton")]
[Trait("Category", "integration")]
public sealed class PasteSaverTests
{
    // 14:23:05 on 18 November 2026, in the clock's own offset.
    private static readonly DateTimeOffset Moment = new(2026, 11, 18, 14, 23, 5, TimeSpan.FromHours(1));

    private static PasteSaver Saver() => new(() => Moment);

    [Fact]
    public void TextIsSavedAsUtf8UnderTheNameOfItsMoment()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");

        PasteSaveResult result = Saver().Save(new PastedText("Café — déjà vu ✓"), folder);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        string saved = Assert.Single(result.SavedPaths);
        Assert.Equal(Path.Combine(folder, "paste-2026-11-18-142305.txt"), saved);
        byte[] bytes = File.ReadAllBytes(saved);
        Assert.Equal(Encoding.UTF8.GetBytes("Café — déjà vu ✓"), bytes);
    }

    [Fact]
    public void ImageBytesAreSavedAsGivenInAPng()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        PasteSaveResult result = Saver().Save(new PastedImage(png), folder);

        Assert.True(result.Succeeded);
        string saved = Assert.Single(result.SavedPaths);
        Assert.Equal("paste-2026-11-18-142305.png", Path.GetFileName(saved));
        Assert.Equal(png, File.ReadAllBytes(saved));
    }

    [Fact]
    public void CopiedFilesAreCopiedUnderTheirOwnNames()
    {
        using var scratch = new ScratchFolder();
        string a = scratch.File("alpha", "source", "notes.md");
        string b = scratch.File("beta", "source", "photo.jpg");
        string folder = scratch.Folder("out");

        PasteSaveResult result = Saver().Save(new PastedFiles([a, b]), folder);

        Assert.True(result.Succeeded);
        Assert.Equal([Path.Combine(folder, "notes.md"), Path.Combine(folder, "photo.jpg")], result.SavedPaths);
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(folder, "notes.md")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(folder, "photo.jpg")));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void ACopiedFolderIsCopiedWholeUnderItsName()
    {
        using var scratch = new ScratchFolder();
        scratch.File("x", "source", "Album", "one.txt");
        scratch.File("y", "source", "Album", "Inner", "two.txt");
        string folder = scratch.Folder("out");

        PasteSaveResult result = Saver().Save(new PastedFiles([Path.Combine(scratch.Path, "source", "Album")]), folder);

        Assert.True(result.Succeeded);
        Assert.Equal([Path.Combine(folder, "Album")], result.SavedPaths);
        Assert.Equal("y", File.ReadAllText(Path.Combine(folder, "Album", "Inner", "two.txt")));
    }

    [Fact]
    public void ATakenNameGetsTheNextNumberAndNothingIsOverwritten()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");
        PasteSaver saver = Saver();

        PasteSaveResult first = saver.Save(new PastedText("one"), folder);
        PasteSaveResult second = saver.Save(new PastedText("two"), folder);
        PasteSaveResult third = saver.Save(new PastedText("three"), folder);

        Assert.Equal("paste-2026-11-18-142305.txt", Path.GetFileName(first.SavedPaths[0]));
        Assert.Equal("paste-2026-11-18-142305 (2).txt", Path.GetFileName(second.SavedPaths[0]));
        Assert.Equal("paste-2026-11-18-142305 (3).txt", Path.GetFileName(third.SavedPaths[0]));
        Assert.Equal("one", File.ReadAllText(first.SavedPaths[0]));
    }

    [Fact]
    public void ACopiedFileWhoseNameIsTakenGetsTheNextNumber()
    {
        using var scratch = new ScratchFolder();
        string source = scratch.File("new", "source", "notes.md");
        string kept = scratch.File("old", "out", "notes.md");

        PasteSaveResult result = Saver().Save(new PastedFiles([source]), Path.GetDirectoryName(kept)!);

        Assert.True(result.Succeeded);
        Assert.Equal("notes (2).md", Path.GetFileName(Assert.Single(result.SavedPaths)));
        Assert.Equal("old", File.ReadAllText(kept));
        Assert.Equal("new", File.ReadAllText(result.SavedPaths[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFolderFailsWithTheReasonAndWritesNothing(string? folder)
    {
        PasteSaveResult result = Saver().Save(new PastedText("hello"), folder);

        Assert.False(result.Succeeded);
        Assert.Equal(PasteSaveErrors.NoFolder, result.Error);
        Assert.Empty(result.SavedPaths);
    }

    [Fact]
    public void AFolderGoneFromTheDiskFailsWithoutThrowing()
    {
        using var scratch = new ScratchFolder();
        string gone = scratch.Folder("gone");
        Directory.Delete(gone);

        PasteSaveResult result = Saver().Save(new PastedImage(new byte[] { 1, 2 }), gone);

        Assert.False(result.Succeeded);
        Assert.Equal(PasteSaveErrors.FolderMissing, result.Error);
        Assert.False(Directory.Exists(gone));
    }

    [Fact]
    public void AnEmptyPasteFailsAsNothingToSave()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");

        Assert.Equal(PasteSaveErrors.NothingToSave, Saver().Save(new PastedText(""), folder).Error);
        Assert.Equal(PasteSaveErrors.NothingToSave, Saver().Save(new PastedImage(ReadOnlyMemory<byte>.Empty), folder).Error);
        Assert.Equal(PasteSaveErrors.NothingToSave, Saver().Save(new PastedFiles([]), folder).Error);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void ACopiedFileGoneStopsThePasteAndKeepsWhatWasCopiedBefore()
    {
        using var scratch = new ScratchFolder();
        string present = scratch.File("a", "source", "a.txt");
        string missing = Path.Combine(scratch.Path, "source", "missing.txt");
        string folder = scratch.Folder("out");

        PasteSaveResult result = Saver().Save(new PastedFiles([present, missing]), folder);

        Assert.False(result.Succeeded);
        Assert.Equal(PasteSaveErrors.SourceMissing, result.Error);
        Assert.Equal([Path.Combine(folder, "a.txt")], result.SavedPaths);
    }

    [Fact]
    public void AFolderPastedIntoItselfFailsWithoutThrowing()
    {
        using var scratch = new ScratchFolder();
        string album = scratch.Folder("Album");
        scratch.File("x", "Album", "one.txt");

        PasteSaveResult result = Saver().Save(new PastedFiles([album]), album);

        Assert.False(result.Succeeded);
        Assert.Equal(PasteSaveErrors.WriteFailed, result.Error);
        Assert.Equal(["one.txt"], Directory.EnumerateFileSystemEntries(album).Select(Path.GetFileName));
    }

    [Fact]
    public void ADefaultSaverNamesThePasteFromTheLocalClock()
    {
        using var scratch = new ScratchFolder();
        string folder = scratch.Folder("out");
        DateTime before = DateTime.Now;

        PasteSaveResult result = new PasteSaver().Save(new PastedText("now"), folder);

        string name = Path.GetFileName(Assert.Single(result.SavedPaths));
        Assert.StartsWith("paste-" + before.ToString("yyyy-MM-dd"), name);
    }
}
