using System.Text.Json;
using Deckle.Core;
using Deckle.Input.Trackpad;
using Xunit;

namespace Deckle.Input.Trackpad.Tests;

// The store runs on a temp file so the test never touches the settings file of
// the singleton. Options() copies TrackpadSettingsService's serializer options
// (they are private), so the round trip covers TrackpadSettings through the
// generic store, not the service itself.
public class TrackpadSettingsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"deckle-trackpad-test-{Guid.NewGuid():N}.json");
    private readonly string _mutex = $"Deckle-Trackpad-Test-{Guid.NewGuid():N}";

    private static JsonSerializerOptions Options() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PressDelay_DefaultsTo50MsInsideThePageRange()
    {
        var s = new TrackpadSettings();
        Assert.Equal(50, s.PressDelayMs);
        Assert.InRange(s.PressDelayMs, TrackpadSettings.MinPressDelayMs, TrackpadSettings.MaxPressDelayMs);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void PressDelay_RoundTripsThroughStore()
    {
        using (var store = new JsonSettingsStore<TrackpadSettings>(_path, _mutex, Options()))
        {
            store.Current.PressDelayMs = 120;
            store.Flush();
        }

        Assert.Contains("\"pressDelayMs\": 120", File.ReadAllText(_path));
        using var reloaded = new JsonSettingsStore<TrackpadSettings>(_path, _mutex, Options());
        Assert.Equal(120, reloaded.Current.PressDelayMs);
    }

    // Every settings file written before the delay became a setting lacks the
    // key; it must load with the delay the engine used to hard-code.
    [Fact]
    [Trait("Category", "integration")]
    public void PressDelay_FileWithoutTheKeyLoadsTheDefault()
    {
        File.WriteAllText(_path, """{ "enabled": true, "dragSpeed": 1.5 }""");

        using var store = new JsonSettingsStore<TrackpadSettings>(_path, _mutex, Options());

        Assert.True(store.Current.Enabled);
        Assert.Equal(1.5, store.Current.DragSpeed);
        Assert.Equal(50, store.Current.PressDelayMs);
        Assert.Equal(new TrackpadSettings().PressDistancePx, store.Current.PressDistancePx);
    }

    [Fact]
    [Trait("Category", "unit")]
    public void PressDistance_DefaultsInsideThePageRange()
    {
        var s = new TrackpadSettings();
        Assert.InRange(s.PressDistancePx, TrackpadSettings.MinPressDistancePx, TrackpadSettings.MaxPressDistancePx);
    }

    [Fact]
    [Trait("Category", "integration")]
    public void PressDistance_RoundTripsThroughStore()
    {
        using (var store = new JsonSettingsStore<TrackpadSettings>(_path, _mutex, Options()))
        {
            store.Current.PressDistancePx = 12;
            store.Flush();
        }

        Assert.Contains("\"pressDistancePx\": 12", File.ReadAllText(_path));
        using var reloaded = new JsonSettingsStore<TrackpadSettings>(_path, _mutex, Options());
        Assert.Equal(12, reloaded.Current.PressDistancePx);
    }
}
