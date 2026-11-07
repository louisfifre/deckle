using System.Text.Json;
using Deckle.Audio.Internal;
using Deckle.Core;

namespace Deckle.Audio;

// ── CaptureSettingsService ────────────────────────────────────────────────
//
// Module-local persistence for CaptureSettings. Twin of
// TranscriptionSettingsService — see that file's comment for the design rationale.
//
// SettingsBootstrap.MigrateLegacyToPerModule() migrates the legacy layouts
// (the combined settings.json, the modules/capture/ folder) into this file.
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
            logError:    msg => DeckleAudioSource.Log.SettingsLoadError(msg),
            postLoadMigration: settings => ApplyPostLoadMigrations(settings));

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

    // The recording limit's former default. In a file older than schema
    // version 1, a stored value equal to it means the user never chose a limit.
    internal const int LegacyDefaultMaxRecordingDurationSeconds = 20 * 60;

    // Runs on every load (and on fresh defaults) through the store's hook,
    // like the twin TranscriptionSettingsService. The hook, not
    // SettingsBootstrap: it stays inside the module, works on the typed POCO
    // and is unit-testable. Returning true makes the store write the result.
    //
    // The SchemaVersion stamp makes the rewrite run once. Without it, a user
    // who later picks 20 minutes on purpose would see that choice wiped back
    // to "no limit" at every launch; each step therefore runs only for a file
    // older than the version that introduced it.
    //
    // resolveLegacyDevice turns a waveIn device index into an endpoint id; the
    // production one asks the driver, a test passes its own.
    internal static bool ApplyPostLoadMigrations(
        CaptureSettings settings, Func<int, string?>? resolveLegacyDevice = null)
    {
        bool changed = false;

        if (settings.SchemaVersion < 1)
        {
            if (settings.MaxRecordingDurationSeconds == LegacyDefaultMaxRecordingDurationSeconds)
                settings.MaxRecordingDurationSeconds = 0;
            changed = true;
        }

        // The microphone moved from a waveIn index to an endpoint id. Keyed on
        // the legacy value being present, not on the version, so a file that
        // somehow carries both is still cleaned.
        if (settings.AudioInputDeviceId is int legacyIndex)
        {
            MigrateLegacyDevice(settings, legacyIndex, resolveLegacyDevice ?? LegacyWaveInDeviceMap.EndpointIdOf);
            settings.AudioInputDeviceId = null;
            changed = true;
        }

        if (settings.SchemaVersion < CaptureSettings.CurrentSchemaVersion)
        {
            settings.SchemaVersion = CaptureSettings.CurrentSchemaVersion;
            changed = true;
        }
        return changed;
    }

    // -1 was the system default, which is what an empty endpoint id means. An
    // index that resolves to no endpoint (the device is gone, or the driver does
    // not answer) falls back to the system default too, and says so: the
    // person's choice could not be carried over.
    private static void MigrateLegacyDevice(
        CaptureSettings settings, int legacyIndex, Func<int, string?> resolve)
    {
        if (legacyIndex < 0 || !string.IsNullOrEmpty(settings.AudioInputEndpointId))
            return;

        string? endpointId = resolve(legacyIndex);
        if (endpointId is null)
        {
            DeckleAudioSource.Log.SettingsLoadWarning(
                $"The saved microphone (waveIn index {legacyIndex}) matches no device any more, so the system default microphone is used");
            return;
        }

        settings.AudioInputEndpointId = endpointId;
        DeckleAudioSource.Log.SettingsLoaded(
            $"The saved microphone (waveIn index {legacyIndex}) was migrated to endpoint {endpointId}");
    }

    public void Save()                      => _store.Save();
    public void Flush()                     => _store.Flush();
    public void Reload()                    => _store.Reload();
    public void Replace(CaptureSettings next) => _store.Replace(next);
}
