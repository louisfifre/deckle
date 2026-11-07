using System.Text.Json;
using Deckle.Core;

namespace Deckle.Audio;

// ── CaptureSettingsService ────────────────────────────────────────────────
//
// Module-local persistence for CaptureSettings. Twin of
// TranscriptionSettingsService — see that file's comment for the design rationale.
public sealed class CaptureSettingsService
{
    private static readonly Lazy<CaptureSettingsService> _instance = new(() => new CaptureSettingsService());
    public static CaptureSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<CaptureSettings> _store;

    public CaptureSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private CaptureSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "audio", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<CaptureSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Audio-Save",
            jsonOptions: _jsonOptions,
            // Info and verbose were not wired, so a load, a reload or a save of
            // the audio settings left no trace and the two events stayed dead.
            logInfo:     msg => DeckleAudioSource.Log.SettingsLoaded(msg),
            logVerbose:  msg => DeckleAudioSource.Log.SettingsLoadComplete(msg),
            logWarning:  msg => DeckleAudioSource.Log.SettingsLoadWarning(msg),
            logError:    msg => DeckleAudioSource.Log.SettingsLoadError(msg));

        // The values every recording reads, once at load and again after each
        // save, reload or external edit, so a take's device and cap can be
        // traced to the settings in force.
        LogResolved(_store.Current);
        _store.Changed += () => LogResolved(_store.Current);
    }

    private static void LogResolved(CaptureSettings settings)
        => DeckleAudioSource.Log.CaptureSettingsResolved(
            settings.AudioInputEndpointId,
            settings.MaxRecordingDurationSeconds,
            settings.Preprocessing?.Enabled ?? false,
            settings.VoiceLevelDbfs);

    public void Save()                      => _store.Save();
    public void Flush()                     => _store.Flush();
    public void Reload()                    => _store.Reload();
    public void Replace(CaptureSettings next) => _store.Replace(next);
}
