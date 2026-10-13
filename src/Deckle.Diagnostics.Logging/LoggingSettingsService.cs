using System;
using System.IO;
using System.Text.Json;
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
// The store's log callbacks are left unwired: it is silent, and a load error
// resets to defaults without a trace.
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
