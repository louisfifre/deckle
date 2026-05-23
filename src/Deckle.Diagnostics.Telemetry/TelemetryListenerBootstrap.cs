using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics.Listeners;

namespace Deckle.Diagnostics.Telemetry;

// Boot-time configuration of the structured-telemetry JsonlEventListeners.
//
// Two static entry points :
//
//   - Configure(...) instancie les quatre JsonlEventListeners (un par
//     destination canonique : app, latency, microphone, corpus). Doit
//     être appelée une seule fois au boot.
//   - ConfigureGates(...) câble le délégué qui lit les toggles
//     utilisateur côté host. Peut être appelée avant ou après
//     Configure ; les prédicates lisent la dernière valeur connue à
//     chaque émission, donc une mise à jour du délégué propage sans
//     reconstruction des listeners.
//
// Pourquoi séparer ? Configure crée les listeners avec leurs prédicates
// figés ; les prédicates doivent consulter une variable mutable qui
// peut changer après l'instanciation (et qui change effectivement
// pendant la sous-vague 6d, où l'App câble une lecture sur le legacy
// AppTelemetryGates plutôt que sur le futur TelemetrySettingsService).
//
// Destinations canoniques :
//   app.jsonl        ← milestones (Level <= Informational) excluding
//                      dedicated structured telemetries
//   latency.jsonl    ← LatencyRecorded events
//   microphone.jsonl ← MicrophoneTelemetryRecorded events
//   corpus.jsonl     ← CorpusRecorded events
//
// Sémantique des gates utilisateur :
//   app.jsonl        ← ApplicationLogToDisk == true
//   latency.jsonl    ← LatencyEnabled == true
//   microphone.jsonl ← MicrophoneTelemetry == true
//   corpus.jsonl     ← CorpusEnabled == true
//
// Posture par défaut : gates closes (false). Tant que ConfigureGates
// n'a pas été appelée, aucune ligne ne touche disque — fail-safe
// reproduisant la posture de l'ancien JsonlFileSink quand AppSettings
// n'était pas encore prêt.
//
// Validation sub-directory. Wave 1 runs the new pipeline alongside
// the legacy JsonlFileSink, which still owns the canonical files at
// <telemetryDir>/{app,latency,microphone,corpus}.jsonl. To avoid
// mixed emissions in the same files during the validation window,
// Configure(...) accepts a `validationSubdirectory` flag that, when
// true, parks the new files under <telemetryDir>/validation/. The
// flag flips to false in Wave 6 when the legacy sink is removed and
// the new pipeline takes over the canonical paths.
public static class TelemetryListenerBootstrap
{
    private static readonly List<EventListener> _listeners = new();
    private static bool _configured;

    // Source de vérité externe pour les gates utilisateur. Null = posture
    // fermée (toute gate retourne false). Câblée par l'App via
    // ConfigureGates ; lue à chaque émission pour que les flips du
    // toggle dans Settings prennent effet immédiatement.
    private static Func<string, bool>? _gateReader;

    public static void Configure(string telemetryDirectory, bool validationSubdirectory = true)
    {
        if (_configured) return;
        _configured = true;

        string rootDirectory = validationSubdirectory
            ? Path.Combine(telemetryDirectory, "validation")
            : telemetryDirectory;

        Directory.CreateDirectory(rootDirectory);

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "app.jsonl"),
            kindLabel: "log",
            predicate: e =>
                   e.EventName != "LatencyRecorded"
                && e.EventName != "MicrophoneTelemetryRecorded"
                && e.EventName != "CorpusRecorded"
                && ReadGate("ApplicationLogToDisk")));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "latency.jsonl"),
            kindLabel: "latency",
            predicate: e => e.EventName == "LatencyRecorded"
                         && ReadGate("LatencyEnabled")));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "microphone.jsonl"),
            kindLabel: "microphone",
            predicate: e => e.EventName == "MicrophoneTelemetryRecorded"
                         && ReadGate("MicrophoneTelemetry")));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "corpus.jsonl"),
            kindLabel: "corpus",
            predicate: e => e.EventName == "CorpusRecorded"
                         && ReadGate("CorpusEnabled")));
    }

    // Câble le délégué de lecture des gates utilisateur. Accepte un
    // nom symbolique (« ApplicationLogToDisk », « LatencyEnabled »,
    // « MicrophoneTelemetry », « CorpusEnabled ») et retourne le bool
    // courant. Un nom inconnu doit retourner false côté caller.
    //
    // Idempotent — réappeler ConfigureGates remplace le délégué. Utile
    // si le host migre de la source legacy vers la nouvelle en un
    // seul swap.
    public static void ConfigureGates(Func<string, bool> gateReader)
    {
        if (gateReader is null) throw new ArgumentNullException(nameof(gateReader));
        _gateReader = gateReader;
    }

    private static bool ReadGate(string gateName)
    {
        var reader = _gateReader;
        if (reader is null) return false;
        try { return reader(gateName); }
        catch { return false; }
    }

    // Tears down every listener registered by Configure. Optional —
    // process exit cleans up anyway, but the method is exposed for
    // tests and for the eventual host shutdown sequence.
    public static void ShutDown()
    {
        foreach (var listener in _listeners) listener.Dispose();
        _listeners.Clear();
        _configured = false;
    }
}
