using System.Text.Json;
using Deckle.Core;
using Deckle.Presets;

namespace Deckle.Lighting.Ambient;

// Module-local persistence for AmbientSettings.
//
// The preset family lives here, one per process, so the Settings page and
// the Playground tab select and edit the same selection.
public sealed class AmbientSettingsService
{
    private static readonly Lazy<AmbientSettingsService> _instance = new(() => new AmbientSettingsService());
    public static AmbientSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Files on disk may carry PascalCase names from before the
        // camelCase policy; reading must still accept them.
        PropertyNameCaseInsensitive = true,
    };

    private readonly JsonSettingsStore<AmbientSettings> _store;

    public AmbientSettings Current => _store.Current;

    public string Path => _store.Path;

    /// <summary>The Ambient presets over <see cref="Current"/>. Built after
    /// the file is read, so a file without a stored selection gets it
    /// derived once from its values (saved with the next edit).</summary>
    public PresetFamily<AmbientTuning> Presets { get; }

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private AmbientSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "ambient", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<AmbientSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Ambient-Save",
            jsonOptions: _jsonOptions,
            logWarning:  msg => DeckleAmbientSource.Log.SettingsLoadWarning(msg),
            logError:    msg => DeckleAmbientSource.Log.SettingsLoadError(msg));

        Presets = new PresetFamily<AmbientTuning>(
            AmbientPresets.All,
            AmbientPresets.Default,
            new AmbientPresetStore(() => Current));
    }

    public void Save()                       => _store.Save();
    public void Flush()                      => _store.Flush();
    public void Reload()                     => _store.Reload();
    public void Replace(AmbientSettings next) => _store.Replace(next);
}
