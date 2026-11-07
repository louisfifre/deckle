using System.Text.Json;
using Deckle.Core;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

// The capture rate left the settings on 2026-11-07: it is decided in code
// (AmbientCaptureCap). A file written before then still carries it. Loading
// must ignore the key without losing a neighbour, and the next save must drop
// it. The store is built the way AmbientSettingsService builds it (same JSON
// options), on a temporary file, since the service singleton touches the real
// data root.
[Trait("Category", "integration")]
public sealed class AmbientSettingsMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "deckle-ambient-migration-" + Guid.NewGuid().ToString("N"));

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AmbientSettingsMigrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private JsonSettingsStore<AmbientSettings> StoreOver(string json, out string path)
    {
        path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);
        return new JsonSettingsStore<AmbientSettings>(
            path, "Deckle-Test-AmbientMigration-" + Guid.NewGuid().ToString("N"), Options);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(15)]
    [InlineData(45)]
    public void AStoredCaptureRateIsIgnoredAndTheOtherValuesSurvive(int storedRate)
    {
        using var store = StoreOver(
            $$"""{ "enabled": true, "useMultiLight": true, "captureFrameRateHz": {{storedRate}}, "smoothingAlpha": 0.5 }""",
            out _);

        Assert.True(store.Current.Enabled);
        Assert.True(store.Current.UseMultiLight);
        Assert.Equal(0.5, store.Current.SmoothingAlpha);
    }

    [Fact]
    public void ThePascalCaseSpellingOfTheOldKeyIsIgnoredToo()
    {
        using var store = StoreOver("""{ "Enabled": true, "CaptureFrameRateHz": 15 }""", out _);

        Assert.True(store.Current.Enabled);
    }

    [Fact]
    public void TheNextSaveDropsTheOldKey()
    {
        using var store = StoreOver("""{ "enabled": true, "captureFrameRateHz": 30 }""", out string path);

        store.Save();
        store.Flush();

        string written = File.ReadAllText(path);
        Assert.DoesNotContain("captureFrameRateHz", written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"enabled\": true", written);
    }
}
