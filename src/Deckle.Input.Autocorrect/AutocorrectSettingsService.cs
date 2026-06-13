using System.Text.Json;
using Deckle.Core;

namespace Deckle.Input.Autocorrect;

// Module-local persistence for AutocorrectSettings — the standard
// JsonSettingsStore<T> lazy singleton at
// <UserDataRoot>/modules/autocorrect/settings.json, same shape as
// TrackpadSettingsService. Store logging callbacks stay null: no
// EventSource dependency from the persistence layer.
public sealed class AutocorrectSettingsService
{
    private static readonly Lazy<AutocorrectSettingsService> _instance =
        new(() => new AutocorrectSettingsService());
    public static AutocorrectSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<AutocorrectSettings> _store;

    public AutocorrectSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private AutocorrectSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "autocorrect", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<AutocorrectSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Autocorrect-Save",
            jsonOptions: _jsonOptions);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(AutocorrectSettings next) => _store.Replace(next);
}
