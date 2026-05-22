using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Telemetry;

// ── TelemetrySettingsService ────────────────────────────────────────────────
//
// Module-local persistence for TelemetrySettings. Successeur du
// Deckle.Logging.TelemetrySettingsService legacy supprimé en sous-vague
// 6g — même JsonSettingsStore<T>, même singleton lazy, et surtout même
// path on-disk (<UserDataRoot>/modules/telemetry/settings.json) pour
// préserver les settings utilisateur existants à travers la bascule.
//
// Consumers : DiagnosticsViewModel (lecture / écriture UI), App
// boot wiring (CorpusPaths.ConfigureStorageDirectoryOverride + Telemetry-
// ListenerBootstrap.ConfigureGates), AppWhispEngineHost (lecture par
// le pipeline transcription).
//
// Logs internes du store. JsonSettingsStore<T> reçoit ses callbacks
// log via lambdas pour rester découplé du pipeline d'observabilité.
// Callbacks à null en 6g : le store reste silencieux ; les erreurs
// critiques de chargement se traduisent par un reset sur defaults.
// L'absence de log d'I/O est acceptable tant que la fréquence d'accès
// reste basse (lecture par tick côté listeners, écriture rare déclenchée
// par les toggles UI). Câbler un provider EventSource nécessiterait soit
// un module dédié, soit la réutilisation du provider Settings via un
// câblage cross-module sans cycle de dépendance — pas urgent.
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

        // Callbacks log volontairement non câblés (cf. commentaire d'en-
        // tête). Le store reste silencieux ; les erreurs critiques de
        // chargement se traduisent par un reset sur defaults.
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
