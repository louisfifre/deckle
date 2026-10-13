using System;
using System.Collections.Generic;
using System.IO;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Behavior tests of the presence-choice persistence: what a Save leaves on disk
// reads back as is, and both degradations (missing file, corrupt file) read as
// "no choice", the all-present fallback the app applies above.
[Trait("Category", "unit")]
public class PresenceFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "Deckle-Tests", Path.GetRandomFileName());

    private string FilePath => Path.Combine(_dir, "presence.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void SaveThenLoadRoundTripsTheChoice()
    {
        PresenceFile.SaveTo(FilePath, ["transcription", "ambient"]);

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.Equal(new HashSet<string> { "transcription", "ambient" }, loaded);
    }

    [Fact]
    public void AMissingFileReadsAsNoChoice()
    {
        Assert.Null(PresenceFile.LoadFrom(FilePath));
    }

    [Fact]
    public void ACorruptFileReadsAsNoChoice()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Null(PresenceFile.LoadFrom(FilePath));
    }

    [Fact]
    public void AnEmptyChoiceIsAChoice()
    {
        PresenceFile.SaveTo(FilePath, []);

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.Empty(loaded);
    }

    [Fact]
    public void SaveCreatesTheParentFolderAndOverwrites()
    {
        PresenceFile.SaveTo(FilePath, ["a"]);
        PresenceFile.SaveTo(FilePath, ["b"]);

        Assert.Equal(new HashSet<string> { "b" }, PresenceFile.LoadFrom(FilePath));
    }
}
