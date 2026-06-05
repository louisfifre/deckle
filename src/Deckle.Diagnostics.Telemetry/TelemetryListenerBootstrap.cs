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
//   - Configure(...) instantiates the canonical JSONL listeners: app, latency,
//     microphone, then two corpus routes (ASR + rewrite). Must be called only
//     once at boot.
//   - ConfigureGates(...) wires the delegate that reads user toggles on the
//     host side. May be called before or after Configure; predicates read the
//     last known value at each emission, so a delegate update propagates
//     without reconstructing listeners.
//   - ConfigureApplicationLogDropFilter(...) wires runtime filters for the
//     application log (e.g. ambient Verbose during capture). The Telemetry
//     module remains independent from Diagnostics.Logging: the host supplies
//     the predicate.
//
// Why separate them? Configure creates listeners with their predicates frozen;
// predicates must consult a mutable variable that can change after
// instantiation when the user changes telemetry toggles.
//
// Canonical destinations:
//   app.jsonl                                      ← rendered application log
//                                                    (readable line + payload),
//                                                    excluding dedicated
//                                                    structured telemetry
//   latency.jsonl                                  ← LatencyRecorded events
//   microphone.jsonl                               ← MicrophoneTelemetryRecorded
//                                                    events (raw)
//   microphone.processed.jsonl                     ← PreprocessedTelemetryRecorded
//                                                    events (post-DSP mirror)
//   corpus/<bucket>/<tier>/corpus.jsonl            ← CorpusAsrRecorded events
//                                                    (routed)
//   corpus/<bucket>/corpus.jsonl                   ← CorpusRewriteRecorded
//                                                    events (routed, no tier;
//                                                    see ADR-0006)
//
// User gate semantics:
//   app.jsonl                  ← ApplicationLogToDisk == true
//   latency.jsonl              ← LatencyEnabled == true
//   microphone.jsonl,
//   microphone.processed.jsonl ← MicrophoneTelemetry == true
//   corpus/raw/…,
//   corpus/rewrite-…/      ← CorpusEnabled == true
//
// Default posture: gates closed (false). Until ConfigureGates has been called,
// no line touches disk: fail-safe behavior reproducing the old JsonlFileSink
// posture when AppSettings was not ready yet.
//
// Validation sub-directory. Configure(...) accepts a
// `validationSubdirectory` flag for isolated comparison runs. Production
// boot passes false so the listeners write the canonical files directly
// under <telemetryDir>/{app,latency,microphone,corpus}.jsonl.
public static class TelemetryListenerBootstrap
{
    private static readonly List<EventListener> _listeners = new();
    private static bool _configured;

    // External source of truth for user gates. Null = closed posture (every
    // gate returns false). Wired by the App through ConfigureGates; read at
    // every emission so toggle flips in Settings take effect immediately.
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
                && e.EventName != "PreprocessedTelemetryRecorded"
                && e.EventName != "CorpusAsrRecorded"
                && e.EventName != "CorpusRewriteRecorded"
                && !ShouldDropApplicationLog(e)
                && ReadGate("ApplicationLogToDisk"),
            preEntryDropPredicate: ShouldDropApplicationLog,
            // app.jsonl is the persistent mirror of the live log:
            // self-describing envelope (provider/event/level/source/message/
            // line), rotated by line chunks into numbered generations in
            // archive/ (never renamed or deleted; the user prunes). Datasets
            // stay PayloadOnly without rotation. Rolled-log /
            // untouched-datasets principle: ADR-0007.
            schema:   JsonlSchema.SelfDescribing,
            rotation: new JsonlRotationPolicy(maxLines: 8000)));

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

        // Post-DSP mirror of microphone.jsonl: the processed signal
        // distribution, same field-for-field schema as raw, in a sibling file
        // that sorts next to it. Two homogeneous files rather than one mixed
        // file: each loads as-is without filtering a discriminant, and
        // processed only exists when the DSP ran. Same consent: emission
        // already gates on MicrophoneTelemetry on the orchestrator side.
        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "microphone.processed.jsonl"),
            kindLabel: "microphone_processed",
            predicate: e => e.EventName == "PreprocessedTelemetryRecorded"
                         && ReadGate("MicrophoneTelemetry")));

        // Normalized corpus: see ADR-0006. Two routed listeners spray
        // CorpusAsr/RewriteRecorded over a bucketed tree. Both predicates gate
        // on CorpusEnabled and the resolver composes the path from the event
        // payload.
        string corpusRoot = Path.Combine(rootDirectory, "corpus");

        _listeners.Add(new RoutedJsonlEventListener(
            pathResolver: e =>
            {
                // The producer guarantees component presence and sanitization;
                // a malformed payload leaves the path empty and the event is
                // silently skipped.
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

    // Wires the user gate reader delegate. Accepts a symbolic name
    // ("ApplicationLogToDisk", "LatencyEnabled", "MicrophoneTelemetry",
    // "CorpusEnabled") and returns the current bool. An unknown name must
    // return false on the caller side.
    //
    // Idempotent: calling ConfigureGates again replaces the delegate. Useful
    // if the host migrates from the legacy source to the new one in a single
    // swap.
    public static void ConfigureGates(Func<string, bool> gateReader)
    {
        if (gateReader is null) throw new ArgumentNullException(nameof(gateReader));
        _gateReader = gateReader;
    }

    // Wires the predicate that removes some events from the persisted
    // application log. The predicate reads the same EventEntry as the LogWindow
    // drop filter, which keeps app.jsonl aligned with the live window without
    // introducing a Telemetry → Logging reference.
    public static void ConfigureApplicationLogDropFilter(Func<EventEntry, bool> filter)
    {
        if (filter is null) throw new ArgumentNullException(nameof(filter));
        _applicationLogDropFilter = filter;
    }

    // Early variant of the app.jsonl filter, evaluated before EventEntry
    // creation when provider + level are enough. The ambient case uses it so
    // the toggle also cuts loop log allocation cost, not only final writing.
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
