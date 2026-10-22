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

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }
}
