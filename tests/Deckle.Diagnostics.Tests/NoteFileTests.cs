using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "integration")]
public sealed class NoteFileTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"deckle-note-{Guid.NewGuid():N}");

    private string NotePath => Path.Combine(_directory, "nested", "note.txt");

    [Fact]
    public void LinesAreAppendedInOrderAndTheFolderIsCreated()
    {
        NoteFile.Append(NotePath, "first", T0);
        NoteFile.Append(NotePath, "second", T0.AddSeconds(1));

        string[] lines = File.ReadAllLines(NotePath);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("| first", lines[0]);
        Assert.EndsWith("| second", lines[1]);
        Assert.StartsWith("2026-10-22 09:00:00.000", lines[0]);
    }

    [Fact]
    public void AFileThatPassedItsCapStartsOverAndKeepsTheNewestLine()
    {
        NoteFile.Append(NotePath, "old", T0, maxBytes: 10);
        NoteFile.Append(NotePath, "new", T0.AddSeconds(1), maxBytes: 10);

        string[] lines = File.ReadAllLines(NotePath);
        Assert.Single(lines);
        Assert.EndsWith("| new", lines[0]);
    }

    [Fact]
    public void ALineThatCannotBeWrittenIsDroppedWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        // A folder where the file should be: the append cannot succeed.
        Directory.CreateDirectory(Path.Combine(_directory, "blocked.txt"));

        NoteFile.Append(Path.Combine(_directory, "blocked.txt"), "lost", T0);
    }

    [Fact]
    public void TakeReturnsTheLinesInOrderSplitAtTheirTimestampAndRemovesTheFile()
    {
        NoteFile.Append(NotePath, "pid=1 | first", T0);
        NoteFile.Append(NotePath, "pid=2 | second", T0.AddSeconds(1));

        var notes = NoteFile.Take(NotePath);

        Assert.Equal(2, notes.Count);
        Assert.StartsWith("2026-10-22 09:00:00.000", notes[0].At);
        Assert.Equal("pid=1 | first", notes[0].Text);
        Assert.Equal("pid=2 | second", notes[1].Text);
        Assert.False(File.Exists(NotePath));
    }

    [Fact]
    public void TakeOfAMissingFileIsEmpty()
    {
        Assert.Empty(NoteFile.Take(NotePath));
    }

    [Fact]
    public void ALineAppendedAfterATakeStartsAFreshFileThatTheNextTakeReturns()
    {
        NoteFile.Append(NotePath, "before", T0);
        NoteFile.Take(NotePath);

        NoteFile.Append(NotePath, "after", T0.AddSeconds(1));

        var notes = NoteFile.Take(NotePath);
        Assert.Equal("after", Assert.Single(notes).Text);
    }

    // A take that died between moving the file and deleting it leaves the moved
    // copy behind: its lines are returned by the next take, before the new ones.
    [Fact]
    public void TakeReturnsTheLeftoverOfAnInterruptedTakeFirst()
    {
        NoteFile.Append(NotePath, "old", T0);
        File.Move(NotePath, NotePath + ".taken");
        NoteFile.Append(NotePath, "new", T0.AddSeconds(1));

        var notes = NoteFile.Take(NotePath);

        Assert.Equal(["old", "new"], notes.Select(n => n.Text));
        Assert.False(File.Exists(NotePath + ".taken"));
    }

    [Fact]
    public void TakeOfAnUnreadablePathDoesNotThrow()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "blocked.txt"));

        Assert.Empty(NoteFile.Take(Path.Combine(_directory, "blocked.txt")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
