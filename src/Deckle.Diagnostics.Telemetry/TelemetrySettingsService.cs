using System;
using System.IO;
using System.Text.Json;
using Deckle.Core;

namespace Deckle.Diagnostics.Telemetry;

// ── TelemetrySettingsService ────────────────────────────────────────────────
//
// Module-local persistence for TelemetrySettings. Twin of the legacy
// Deckle.Logging.TelemetrySettingsService — same JsonSettingsStore<T>
// pattern, same singleton lazy. Backing file lives at
// <UserDataRoot>/modules/diagnostics-telemetry/settings.json, aligned
// on the placement documented in this module's CLAUDE.md and on the
// canonical sibling layout (modules/<id>/settings.json).
//
// Sous-vague 6d note. Ce service est scaffold uniquement — il n'est
// pas instancié au boot et ne supplante pas encore le legacy. La
// bascule effective intervient en sous-vague 6g, au moment du retrait
// de Deckle.Logging : à ce moment-là, AppTelemetryGates et le
// TelemetrySettingsService legacy disparaissent, et les gate readers
// de TelemetryListenerBootstrap pointeront sur ce service. Tant que
// le legacy vit, instancier ce service en parallèle créerait deux
// fichiers de settings avec divergence possible.
//
// Logs internes du store. JsonSettingsStore<T> reçoit ses callbacks
// log via lambdas pour rester découplé du pipeline d'observabilité.
// On laisse les callbacks à null en sous-vague 6d : le store
// n'émettra rien en attendant que la sous-vague 6g cable un provider
// EventSource adéquat (le candidat naturel sera un DeckleDiagnostics-
// TelemetrySource dédié, ou la réutilisation du provider Settings via
// un câblage cross-module sans cycle de dépendance). Une ProjectReference
// vers Deckle.Settings ici créerait un cycle (Deckle.Settings → Deckle.
// Diagnostics → Deckle.Diagnostics.Telemetry).
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
            AppPaths.UserDataRoot, "modules", "diagnostics-telemetry", "settings.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Callbacks log volontairement non câblés en sous-vague 6d
        // (cf. commentaire d'en-tête). Le store reste silencieux ; les
        // erreurs critiques de chargement se traduiront par un reset
        // sur defaults — acceptable tant que le service n'est pas
        // sollicité runtime.
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
