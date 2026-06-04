using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Logging;

// ── LoggingSettingsService ──────────────────────────────────────────────────
//
// Module-local persistence for LoggingSettings. Successor of the legacy
// Deckle.Logging.LoggingSettingsService removed in sub-wave 6g: same
// JsonSettingsStore<T>, same lazy singleton, and above all the same on-disk
// path (<UserDataRoot>/modules/logging/settings.json) to preserve existing user
// settings through the switch.
//
// Main consumers: LogWindowEventListener drop filter and app.jsonl predicate
// wired at boot by App (read LogAmbientCaptureActivity on each event). Uncached
// read: flipping the toggle in Settings takes effect on the next emission.
// JsonSettingsStore keeps an in-memory snapshot, so access cost is negligible.
//
// Internal store logs. Like TelemetrySettingsService, log callbacks remain null
// in 6g to stay decoupled from the observability pipeline: no consumable
// EventSource provider without a dependency cycle toward Settings. The store
// will be silent; a critical load error will become a reset to defaults.
public sealed class LoggingSettingsService
{
    private static readonly Lazy<LoggingSettingsService> _instance =
        new(() => new LoggingSettingsService());
    public static LoggingSettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly JsonSettingsStore<LoggingSettings> _store;

    public LoggingSettings Current => _store.Current;

    public string Path => _store.Path;

    public event Action? Changed
    {
        add    => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    private LoggingSettingsService()
    {
        string path = System.IO.Path.Combine(
            AppPaths.UserDataRoot, "modules", "logging", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        _store = new JsonSettingsStore<LoggingSettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-Logging-Save",
            jsonOptions: _jsonOptions);
    }

    public void Save()                          => _store.Save();
    public void Flush()                         => _store.Flush();
    public void Reload()                        => _store.Reload();
    public void Replace(LoggingSettings next)   => _store.Replace(next);
}
