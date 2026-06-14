using System.Text.Json;
using Deckle.Core;

namespace Deckle.Speech;

// Module-local persistence for SpeechSettings. Twin of AmbientSettingsService /
// CaptureSettingsService — same JsonSettingsStore pattern, same lazy singleton,
// same naming convention. Backing file:
// <UserDataRoot>/modules/speech/settings.json.
public sealed class SpeechSettingsService
{
    private static readonly Lazy<SpeechSettingsService> _instance = new(() => new SpeechSettingsService());
    public static SpeechSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Tolerate case differences when reading legacy/hand-edited files.
        PropertyNameCaseInsensitive = true,
    };

    private readonly JsonSettingsStore<SpeechSettings> _store;

    public SpeechSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private SpeechSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "speech", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<SpeechSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Speech-Save",
            jsonOptions: _jsonOptions,
            logInfo:     msg => DeckleSpeechSource.Log.SettingsLoaded($"[speech] {msg}"),
            logVerbose:  msg => DeckleSpeechSource.Log.SettingsLoadComplete($"[speech] {msg}"),
            logWarning:  msg => DeckleSpeechSource.Log.SettingsLoadWarning($"[speech] {msg}"),
            logError:    msg => DeckleSpeechSource.Log.SettingsLoadError($"[speech] {msg}"));
    }

    public void Save()                       => _store.Save();
    public void Flush()                      => _store.Flush();
    public void Reload()                     => _store.Reload();
    public void Replace(SpeechSettings next) => _store.Replace(next);
}
