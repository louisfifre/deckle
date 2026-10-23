using Deckle.Diagnostics;
using Xunit;

namespace Deckle.Diagnostics.Tests;

// An install from before v0.68.1 holds the log as journal.jsonl and the trace as
// app.jsonl. The first start after the update gives them, and their archives,
// the current names, so each history reads as one file.
[Trait("Category", "integration")]
public sealed class DiagnosticsFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"deckle-files-{Guid.NewGuid():N}");

    public DiagnosticsFilesTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Legacy_files_and_their_archives_take_the_current_names()
    {
        Write("journal.jsonl", "log");
        Write(Path.Combine("archive", "journal.jsonl.0001"), "log 1");
        Write("app.jsonl", "trace");
        Write(Path.Combine("archive", "app.jsonl.0007"), "trace 7");

        LegacyFileRename result = DiagnosticsFiles.RenameLegacyFiles(_dir);

        Assert.Equal(4, result.Renamed);
        Assert.Null(result.Failure);
        Assert.Equal("log", Read(DiagnosticsFiles.Log));
        Assert.Equal("log 1", Read(Path.Combine("archive", "log.jsonl.0001")));
        Assert.Equal("trace", Read(DiagnosticsFiles.Trace));
        Assert.Equal("trace 7", Read(Path.Combine("archive", "trace.jsonl.0007")));
        Assert.False(File.Exists(Path.Combine(_dir, "journal.jsonl")));
        Assert.False(File.Exists(Path.Combine(_dir, "app.jsonl")));
    }

    [Fact]
    public void A_family_the_current_name_already_owns_is_left_alone()
    {
        Write("journal.jsonl", "old log");
        Write(Path.Combine("archive", "log.jsonl.0001"), "new log 1");

        LegacyFileRename result = DiagnosticsFiles.RenameLegacyFiles(_dir);

        Assert.Equal(0, result.Renamed);
        Assert.Equal("old log", Read("journal.jsonl"));
        Assert.False(File.Exists(Path.Combine(_dir, DiagnosticsFiles.Log)));
    }

    [Fact]
    public void A_fresh_install_has_nothing_to_rename()
    {
        LegacyFileRename result = DiagnosticsFiles.RenameLegacyFiles(_dir);

        Assert.Equal(0, result.Renamed);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Only_numbered_generations_count_as_archives()
    {
        Write(Path.Combine("archive", "app.jsonl.bak"), "not a generation");

        LegacyFileRename result = DiagnosticsFiles.RenameLegacyFiles(_dir);

        Assert.Equal(0, result.Renamed);
        Assert.Equal("not a generation", Read(Path.Combine("archive", "app.jsonl.bak")));
    }

    [Fact]
    public void A_file_that_cannot_move_stops_the_pass_and_is_named()
    {
        Write("journal.jsonl", "log");
        using var held = new FileStream(
            Path.Combine(_dir, "journal.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);

        LegacyFileRename result = DiagnosticsFiles.RenameLegacyFiles(_dir);

        Assert.IsAssignableFrom<IOException>(result.Failure);
        Assert.Equal("journal.jsonl", result.FailedFile);
        Assert.Equal(0, result.Renamed);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_dir, relative));
}
