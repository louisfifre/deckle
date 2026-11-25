using System.Text.Json;
using Deckle.Core;

namespace Deckle.Lighting.Ambient;

// Module-local persistence for AmbientSettings.
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
    }

    public void Save()                       => _store.Save();
    public void Flush()                      => _store.Flush();
    public void Reload()                     => _store.Reload();
    public void Replace(AmbientSettings next) => _store.Replace(next);
}
