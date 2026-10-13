using System.Text.Json;
using Deckle.Core;

namespace Deckle.Input.Trackpad;

// Store logging callbacks stay null: the persistence layer takes no
// EventSource dependency.
public sealed class TrackpadSettingsService
{
    private static readonly Lazy<TrackpadSettingsService> _instance =
        new(() => new TrackpadSettingsService());
    public static TrackpadSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<TrackpadSettings> _store;

    public TrackpadSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private TrackpadSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "trackpad", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<TrackpadSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Trackpad-Save",
            jsonOptions: _jsonOptions);
    }

    public void Save()                          => _store.Save();
    public void Flush()                         => _store.Flush();
    public void Reload()                        => _store.Reload();
    public void Replace(TrackpadSettings next)  => _store.Replace(next);
}
