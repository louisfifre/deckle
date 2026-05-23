using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Logging;

// ── LoggingSettingsService ──────────────────────────────────────────────────
//
// Module-local persistence for LoggingSettings. Successeur du
// Deckle.Logging.LoggingSettingsService legacy supprimé en sous-vague 6g
// — même JsonSettingsStore<T>, même singleton lazy, et surtout même path
// on-disk (<UserDataRoot>/modules/logging/settings.json) pour préserver
// les settings utilisateur existants à travers la bascule.
//
// Consumer principal : le drop filter du LogWindowEventListener câblé au
// boot par App (lecture de LogAmbientCaptureActivity à chaque event).
// Lecture uncached — flipper le toggle dans Settings prend effet à la
// prochaine émission. JsonSettingsStore garde un snapshot en mémoire,
// donc le coût d'accès est négligeable.
//
// Logs internes du store. Comme TelemetrySettingsService, les callbacks
// log restent à null en 6g pour rester découplé du pipeline d'observabi-
// lité — pas de provider EventSource consommable sans cycle de dépendance
// vers Settings. Le store sera silencieux ; une erreur critique de
// chargement se traduira par un reset sur defaults.
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
