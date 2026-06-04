using System.Text.Json;
using Deckle.Core;

namespace Deckle.Audio;

// ── CaptureSettingsService ────────────────────────────────────────────────
//
// Module-local persistence for CaptureSettings. Twin of
// TranscriptionSettingsService — see that file's comment for the design rationale.
//
// Backing file: <UserDataRoot>/modules/audio/settings.json. Migration
// from the legacy combined settings.json and from the previous
// modules/capture/ per-module layout is handled by
// SettingsBootstrap.MigrateLegacyToPerModule(). Two historical renames
// converge on the current `audio` id: `recording` → `capture`
// (2026-05-02 module extraction from Deckle.Transcription) and `capture` →
// `audio` (2026-05-15 module rename when the false-generic `Capture`
// name was retired in favour of an honest audio-domain name).
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
            // EventSource switch. The "[audio]" label that prefixed legacy
            // messages disappears because the source tag on the LogWindow side
            // now comes from the provider name ("Deckle.Audio" → AUDIO), no
            // longer LogSource.Settings. Delegates remain Action<string> to
            // avoid touching the JsonSettingsStore<T> contract before the wave
            // 4 SettingsHost redesign.
            logInfo:     msg => DeckleAudioSource.Log.SettingsLoaded(msg),
            logVerbose:  msg => DeckleAudioSource.Log.SettingsLoadComplete(msg),
            logWarning:  msg => DeckleAudioSource.Log.SettingsLoadWarning(msg),
            logError:    msg => DeckleAudioSource.Log.SettingsLoadError(msg));
    }

    public void Save()                      => _store.Save();
    public void Flush()                     => _store.Flush();
    public void Reload()                    => _store.Reload();
    public void Replace(CaptureSettings next) => _store.Replace(next);
}
