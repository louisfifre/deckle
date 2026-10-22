using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Behavior tests of the presence-choice persistence: what a Save leaves on disk
// reads back as is, both degradations (missing file, corrupt file) read as "no
// choice", the all-present fallback the app applies above, and a file written
// before the catalogue grew keeps reading as the choice it was.
[Trait("Category", "unit")]
public class PresenceFileTests : IDisposable
{
    // What a version-1 file knew of, written as the strings those files hold, so
    // a changed id value breaks here instead of silently changing what old files
    // mean.
    private static readonly string[] Version1Catalogue =
    [
        "transcription", "rewrite", "autocorrect", "ambient", "trackpad", "anytype",
    ];

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
        PresenceFile.SaveTo(
            FilePath, ["transcription", "ambient"], ["transcription", "ambient", "rewrite"]);

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.Equal(new HashSet<string> { "transcription", "ambient" }, loaded.Present);
        Assert.Equal(new HashSet<string> { "transcription", "ambient", "rewrite" }, loaded.Known);
    }

    [Fact]
    public void SaveWritesTheVersion2Shape()
    {
        PresenceFile.SaveTo(FilePath, ["ambient"], ["ambient", "rewrite"]);

        using var json = JsonDocument.Parse(File.ReadAllText(FilePath));

        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("known").GetArrayLength());
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
        PresenceFile.SaveTo(FilePath, [], ["ambient"]);

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Present);
        Assert.False(loaded.Includes("ambient"));
    }

    [Fact]
    public void SaveCreatesTheParentFolderAndOverwrites()
    {
        PresenceFile.SaveTo(FilePath, ["a"], ["a", "b"]);
        PresenceFile.SaveTo(FilePath, ["b"], ["a", "b"]);

        Assert.Equal(new HashSet<string> { "b" }, PresenceFile.LoadFrom(FilePath)?.Present);
    }

    [Fact]
    public void AVersion1FileReadsAgainstTheCatalogueItWasWrittenFor()
    {
        WriteVersion1("transcription", "ambient");

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.Equal(new HashSet<string> { "transcription", "ambient" }, loaded.Present);
        Assert.Equal(new HashSet<string>(Version1Catalogue), loaded.Known);
    }

    [Fact]
    public void AVersion1FileKeepsItsDeclinedModulesAbsent()
    {
        WriteVersion1("transcription", "ambient");

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.True(loaded.Includes(ModuleIds.Ambient));
        Assert.False(loaded.Includes(ModuleIds.Trackpad));
        Assert.False(loaded.Includes(ModuleIds.Anytype));
    }

    // The modules that became presence modules after version 1 were always on
    // for every existing install; a version-1 file must not switch them off.
    [Theory]
    [InlineData(ModuleIds.PrecisionScroll)]
    [InlineData(ModuleIds.Orchestration)]
    public void AVersion1FileKeepsAModuleThatBecameOptionalAfterItPresent(string id)
    {
        WriteVersion1("transcription", "ambient");

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.True(loaded.Includes(id));
    }

    [Fact]
    public void AVersion1FileKeepsAModuleAddedAfterItPresent()
    {
        WriteVersion1("transcription", "ambient");

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.True(loaded.Includes("added-after-version-1"));
    }

    [Fact]
    public void ASavedChoiceKeepsAModuleAddedAfterItPresentAndADeclinedOneAbsent()
    {
        PresenceFile.SaveTo(FilePath, ["ambient"], ["ambient", "rewrite"]);

        var loaded = PresenceFile.LoadFrom(FilePath);

        Assert.NotNull(loaded);
        Assert.False(loaded.Includes("rewrite"));
        Assert.True(loaded.Includes("added-after-the-save"));
    }

    // The shape the app wrote before the file recorded its catalogue, byte for
    // byte what an existing install still has on disk.
    private void WriteVersion1(params string[] present)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            FilePath,
            JsonSerializer.Serialize(new { version = 1, present }));
    }
}
