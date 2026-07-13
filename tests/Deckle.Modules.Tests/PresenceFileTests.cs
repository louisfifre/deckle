using System;
using System.Collections.Generic;
using System.IO;
using Deckle.Modules;
using Xunit;

namespace Deckle.Modules.Tests;

// Tests de comportement sur la persistance du choix de présence : ce qu'un
// Save laisse sur disque doit se relire tel quel, et les deux dégradations
// (fichier absent, fichier corrompu) doivent se lire comme « pas de choix » —
// le repli tout-présent que l'app interprète au-dessus.
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
