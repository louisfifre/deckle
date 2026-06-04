using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Telemetry;

// ── TelemetrySettingsService ────────────────────────────────────────────────
//
// Module-local persistence for TelemetrySettings. Successor of the legacy
// Deckle.Logging.TelemetrySettingsService removed in sub-wave 6g: same
// JsonSettingsStore<T>, same lazy singleton, and above all the same on-disk
// path (<UserDataRoot>/modules/telemetry/settings.json) to preserve existing
// user settings through the switch.
//
// Consumers: DiagnosticsViewModel (UI read/write), App boot wiring
// (CorpusPaths.ConfigureStorageDirectoryOverride +
// TelemetryListenerBootstrap.ConfigureGates), AppTranscriptionEngineHost
// (read by the transcription pipeline).
//
// Internal store logs. JsonSettingsStore<T> receives its log callbacks through
// lambdas to stay decoupled from the observability pipeline. Callbacks are null
// in 6g: the store stays silent; critical load errors become a reset to
// defaults. Lack of I/O logging is acceptable while access frequency stays low
// (read per tick on the listener side, rare writes triggered by UI toggles).
// Wiring an EventSource provider would require either a dedicated module or
// reusing the Settings provider through cross-module wiring without a
// dependency cycle; not urgent.
public sealed class TelemetrySettingsService
{
    private static readonly Lazy<TelemetrySettingsService> _instance =
        new(() => new TelemetrySettingsService());
    public static TelemetrySettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<TelemetrySettings> _store;

    public TelemetrySettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private TelemetrySettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "telemetry", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Log callbacks deliberately unwired (see header comment). The store
        // stays silent; critical load errors become a reset to defaults.
        _store = new JsonSettingsStore<TelemetrySettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-DiagnosticsTelemetry-Save",
            jsonOptions: _jsonOptions);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(TelemetrySettings next)   => _store.Replace(next);
}
