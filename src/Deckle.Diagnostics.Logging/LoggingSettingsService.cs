using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Deckle.Core;

namespace Deckle.Diagnostics.Logging;

// Module-local persistence for LoggingSettings. The on-disk path
// (<UserDataRoot>/modules/logging/settings.json) is where existing user
// settings live; changing it loses them.
//
// Main consumer: producer-side OperationalLogAdmission, wired at boot by App.
// Uncached read: flipping the toggle in Settings takes effect on the next
// emission. JsonSettingsStore keeps an in-memory snapshot, so access cost is
// negligible.
//
// The store reports through DeckleDiagnosticsSource. A report made while the
// store is still loading (a damaged file at boot) is handed to the thread pool:
// the sinks read this service's Current, and reading it from inside its own
// construction would re-enter the Lazy and fault it for good.
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
            jsonOptions: _jsonOptions,
            logWarning:  msg => Report(msg, DeckleDiagnosticsSource.Log.SettingsLoadWarning),
            logError:    msg => Report(msg, DeckleDiagnosticsSource.Log.SettingsLoadError),
            preDeserializeMigration: MigrateDetailToggles);
    }

    // The controls were on/off "detail" toggles before they took levels
    // (2026-11-07). A toggle that was on keeps everything it admitted:
    // Details. One that was off becomes Summary, the new default, so the
    // activity now leaves its heartbeat or rollup where it left nothing;
    // Windowing has no summary, so off stays Off there. A level already in
    // the file wins over a stale toggle beside it.
    private static readonly (string Toggle, string Level, string Off)[] LegacyToggles =
    [
        ("logAmbientCaptureActivity", "ambientVerbosity",       nameof(OperationalLogVerbosity.Summary)),
        ("logTranscriptionActivity",  "transcriptionVerbosity", nameof(OperationalLogVerbosity.Summary)),
        ("logAutocorrectActivity",    "autocorrectVerbosity",   nameof(OperationalLogVerbosity.Summary)),
        ("logInputActivity",          "inputVerbosity",         nameof(OperationalLogVerbosity.Summary)),
        ("logWindowingActivity",      "windowingVerbosity",     nameof(OperationalLogVerbosity.Off)),
    ];

    public static (string json, bool migrated) MigrateDetailToggles(string json)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject root)
            return (json, false);

        bool migrated = false;
        foreach ((string toggle, string level, string off) in LegacyToggles)
        {
            if (!root.TryGetPropertyValue(toggle, out var node)) continue;
            if (!root.ContainsKey(level))
            {
                bool on = node is not null
                    && node.GetValueKind() == JsonValueKind.True;
                root[level] = on ? nameof(OperationalLogVerbosity.Details) : off;
            }
            root.Remove(toggle);
            migrated = true;
        }

        return migrated ? (root.ToJsonString(), true) : (json, false);
    }

    private void Report(string message, Action<string> emit)
    {
        if (_store is null) ThreadPool.QueueUserWorkItem(_ => emit(message));
        else emit(message);
    }

    public void Save()                          => _store.Save();
    public void Flush()                         => _store.Flush();
    public void Reload()                        => _store.Reload();
    public void Replace(LoggingSettings next)   => _store.Replace(next);
}
