using System.Text.Json;
using Deckle.Core;

namespace Deckle.Input;

// Module-local persistence for MouseWheelSettings — the standard
// JsonSettingsStore<T> lazy singleton at
// <UserDataRoot>/modules/mousewheel/settings.json, same shape as
// TrackpadSettingsService.
public sealed class MouseWheelSettingsService
{
    private static readonly Lazy<MouseWheelSettingsService> _instance =
        new(() => new MouseWheelSettingsService());
    public static MouseWheelSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<MouseWheelSettings> _store;

    public MouseWheelSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private MouseWheelSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "mousewheel", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<MouseWheelSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-MouseWheel-Save",
            jsonOptions: _jsonOptions);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(MouseWheelSettings next)  => _store.Replace(next);
}
