using System.Text.Json;
using Deckle.Composition;
using Deckle.Core;

namespace Deckle.Hud;

// The one place the live HUD takes its stroke and countdown configs from, and
// the one way the Playground reaches the live HUD: an explicit Save of what its
// sliders show. Playing with the sliders changes the preview only.
//
// The store keeps overrides, not whole configs (HudSettings). Effective*()
// lays them over the compiled defaults; Save() replaces them with what differs
// between the compiled defaults and the configs it receives, so a field moved
// back to its default leaves the file.
public sealed class HudSettingsService
{
    private static readonly Lazy<HudSettingsService> _instance =
        new(() => new HudSettingsService());
    public static HudSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<HudSettings> _store;

    public string Path => _store.Path;

    /// <summary>Raised after a disk write; the HUD rebuilds its visuals on it.</summary>
    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private HudSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "hud", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<HudSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Hud-Save",
            jsonOptions: _jsonOptions);
    }

    public HudComposition.ConicArcStrokeConfig EffectiveStroke() =>
        ConfigOverrides.Apply(new HudComposition.ConicArcStrokeConfig(), _store.Current.Stroke, _jsonOptions);

    public UtteranceCountdownConfig EffectiveCountdown() =>
        ConfigOverrides.Apply(new UtteranceCountdownConfig(), _store.Current.Countdown, _jsonOptions);

    /// <summary>
    /// Persist what differs from the compiled defaults in both configs. One
    /// debounced write; Changed fires once it lands.
    /// </summary>
    public void Save(HudComposition.ConicArcStrokeConfig stroke, UtteranceCountdownConfig countdown)
    {
        var current = _store.Current;
        current.Stroke    = ConfigOverrides.Diff(new HudComposition.ConicArcStrokeConfig(), stroke, _jsonOptions);
        current.Countdown = ConfigOverrides.Diff(new UtteranceCountdownConfig(), countdown, _jsonOptions);
        _store.Save();
    }

    public void Flush()  => _store.Flush();
    public void Reload() => _store.Reload();
}
