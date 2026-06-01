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
//   - Configure(...) instancie les listeners JSONL canoniques : app,
//     latency, microphone, puis deux routes corpus (ASR + rewrite).
//     Doit être appelée une seule fois au boot.
//   - ConfigureGates(...) câble le délégué qui lit les toggles
//     utilisateur côté host. Peut être appelée avant ou après
//     Configure ; les prédicates lisent la dernière valeur connue à
//     chaque émission, donc une mise à jour du délégué propage sans
//     reconstruction des listeners.
//   - ConfigureApplicationLogDropFilter(...) câble les filtres runtime
//     du journal applicatif (ex. Verbose ambient pendant capture). Le
//     module Telemetry reste indépendant de Diagnostics.Logging : le
//     host fournit le prédicat.
//
// Pourquoi séparer ? Configure crée les listeners avec leurs prédicates
// figés ; les prédicates doivent consulter une variable mutable qui
// peut changer après l'instanciation quand l'utilisateur modifie les
// toggles de telemetry.
//
// Destinations canoniques :
//   app.jsonl                                      ← journal applicatif
//                                                    rendu (ligne lisible
//                                                    + payload), excluant les
//                                                    télémétries structurées
//                                                    dédiées
//   latency.jsonl                                  ← LatencyRecorded events
//   microphone.jsonl                               ← MicrophoneTelemetryRecorded
//                                                    events
//   corpus/<bucket>/<tier>/corpus.jsonl            ← CorpusAsrRecorded events
//                                                    (routés)
//   corpus/<bucket>/corpus.jsonl                   ← CorpusRewriteRecorded
//                                                    events (routés, pas de
//                                                    tier — voir ADR-0006)
//
// Sémantique des gates utilisateur :
//   app.jsonl              ← ApplicationLogToDisk == true
//   latency.jsonl          ← LatencyEnabled == true
//   microphone.jsonl       ← MicrophoneTelemetry == true
//   corpus/raw/…,
//   corpus/rewrite-…/      ← CorpusEnabled == true
//
// Posture par défaut : gates closes (false). Tant que ConfigureGates
// n'a pas été appelée, aucune ligne ne touche disque — fail-safe
// reproduisant la posture de l'ancien JsonlFileSink quand AppSettings
// n'était pas encore prêt.
//
// Validation sub-directory. Configure(...) accepts a
// `validationSubdirectory` flag for isolated comparison runs. Production
// boot passes false so the listeners write the canonical files directly
// under <telemetryDir>/{app,latency,microphone,corpus}.jsonl.
public static class TelemetryListenerBootstrap
{
    private static readonly List<EventListener> _listeners = new();
    private static bool _configured;

    // Source de vérité externe pour les gates utilisateur. Null = posture
    // fermée (toute gate retourne false). Câblée par l'App via
    // ConfigureGates ; lue à chaque émission pour que les flips du
    // toggle dans Settings prennent effet immédiatement.
    private static Func<string, bool>? _gateReader;
    private static Func<EventEntry, bool>? _applicationLogDropFilter;
    private static Func<string, EventLevel, EventKeywords, bool>? _applicationLogProviderLevelDropFilter;

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
                && e.EventName != "CorpusAsrRecorded"
                && e.EventName != "CorpusRewriteRecorded"
                && !ShouldDropApplicationLog(e)
                && ReadGate("ApplicationLogToDisk"),
            preEntryDropPredicate: ShouldDropApplicationLog,
            // app.jsonl est le miroir persistant du journal live : enveloppe
            // auto-descriptive (provider/event/level/source/message/line)
            // et bornée par rotation. Les datasets restent en PayloadOnly
            // sans rotation (contrat figé, ADR-0006). Décision et bornes :
            // ADR-0007.
            schema:   JsonlSchema.SelfDescribing,
            rotation: new JsonlRotationPolicy(maxBytes: 5 * 1024 * 1024, maxGenerations: 5)));

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

        // Corpus normalisé — voir ADR-0006. Deux listeners routés qui
        // pulvérisent CorpusAsr/RewriteRecorded sur une arborescence
        // bucketée. Le predicate des deux gate sur CorpusEnabled et le
        // resolver compose le path à partir du payload de l'event.
        string corpusRoot = Path.Combine(rootDirectory, "corpus");

        _listeners.Add(new RoutedJsonlEventListener(
            pathResolver: e =>
            {
                // Le producer garantit la présence et la sanitation des
                // composants ; un payload mal formé laisse le path vide
                // et l'event est silencieusement skipé.
                string bucket = e.Payload.TryGetValue("bucket", out var b) ? b?.ToString() ?? "" : "";
                string tier   = e.Payload.TryGetValue("tier",   out var t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(tier)) return "";
                return Path.Combine(corpusRoot, bucket, tier, "corpus.jsonl");
            },
            kindLabel: "corpus_asr",
            predicate: e => e.EventName == "CorpusAsrRecorded"
                         && ReadGate("CorpusEnabled")));

        _listeners.Add(new RoutedJsonlEventListener(
            pathResolver: e =>
            {
                string bucket = e.Payload.TryGetValue("bucket", out var b) ? b?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(bucket)) return "";
                return Path.Combine(corpusRoot, bucket, "corpus.jsonl");
            },
            kindLabel: "corpus_rewrite",
            predicate: e => e.EventName == "CorpusRewriteRecorded"
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

    // Câble le prédicat qui retire certains events du journal applicatif
    // persisté. Le prédicat lit le même EventEntry que le LogWindow
    // drop filter, ce qui garde l'app.jsonl aligné avec la fenêtre live
    // sans introduire de référence de Telemetry vers Logging.
    public static void ConfigureApplicationLogDropFilter(Func<EventEntry, bool> filter)
    {
        if (filter is null) throw new ArgumentNullException(nameof(filter));
        _applicationLogDropFilter = filter;
    }

    // Variante précoce du filtre app.jsonl, évaluée avant la création
    // d'EventEntry quand provider + level suffisent. Le cas ambient
    // l'utilise pour que le toggle coupe aussi le coût d'allocation des
    // logs de boucle, pas seulement l'écriture finale.
    public static void ConfigureApplicationLogProviderLevelDropFilter(
        Func<string, EventLevel, EventKeywords, bool> filter)
    {
        if (filter is null) throw new ArgumentNullException(nameof(filter));
        _applicationLogProviderLevelDropFilter = filter;
    }

    private static bool ReadGate(string gateName)
    {
        var reader = _gateReader;
        if (reader is null) return false;
        try { return reader(gateName); }
        catch { return false; }
    }

    private static bool ShouldDropApplicationLog(EventEntry entry)
    {
        var filter = _applicationLogDropFilter;
        if (filter is null) return false;
        try { return filter(entry); }
        catch { return false; }
    }

    private static bool ShouldDropApplicationLog(EventWrittenEventArgs eventData)
    {
        string? provider = eventData.EventSource.Name;
        if (provider is null) return false;

        var filter = _applicationLogProviderLevelDropFilter;
        if (filter is null) return false;
        try { return filter(provider, eventData.Level, eventData.Keywords); }
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
        _gateReader = null;
        _applicationLogDropFilter = null;
        _applicationLogProviderLevelDropFilter = null;
    }
}
