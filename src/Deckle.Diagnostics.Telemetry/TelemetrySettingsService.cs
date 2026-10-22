using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Telemetry;

// Module-local persistence for TelemetrySettings. The on-disk path
// (<UserDataRoot>/modules/telemetry/settings.json) is where existing user
// settings live; changing it loses them.
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

        _store = new JsonSettingsStore<TelemetrySettings>(
            path:        path,
            mutexName:   $"{AppPaths.AppFolderName}-Settings-DiagnosticsTelemetry-Save",
            jsonOptions: _jsonOptions,
            logWarning:  DeckleDiagnosticsSource.Log.SettingsLoadWarning,
            logError:    DeckleDiagnosticsSource.Log.SettingsLoadError);
    }

    public void Save()                            => _store.Save();
    public void Flush()                           => _store.Flush();
    public void Reload()                          => _store.Reload();
    public void Replace(TelemetrySettings next)   => _store.Replace(next);
}
