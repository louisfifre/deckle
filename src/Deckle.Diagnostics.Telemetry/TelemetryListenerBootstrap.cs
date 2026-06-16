using System;
using System.Collections.Generic;
using System.IO;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Telemetry;

// Boot-time configuration of the structured-telemetry JSONL sinks. Since the
// dispatch refonte these are passive ILogSinks: this module builds them and
// registers them on the host's single DispatchEventListener, which gates and
// builds each EventEntry before offering it. The module no longer creates any
// EventListener of its own.
//
// Entry points :
//
//   - Configure(dispatch, …) builds the canonical JSONL sinks (app, latency,
//     microphone, processed, then two corpus routes) and registers them on the
//     passed-in dispatcher. Must be called only once at boot.
//   - ConfigureGates(...) wires the delegate that reads user toggles on the
//     host side. May be called before or after Configure; predicates read the
//     last known value at each emission, so a delegate update propagates
//     without rebuilding sinks.
//   - ConfigureApplicationLogDropFilter(...) wires the entry-level projection
//     filter for the application log (e.g. sharing the LogWindow Activity lens).
//     The capture-Verbose silencing is NOT here — that is the dispatcher's
//     central gate, applied once for every sink. This module keeps only the
//     app.jsonl-specific entry-level filter, and stays independent from
//     Deckle.Diagnostics.Logging: the host supplies the predicate.
//
// Why separate Configure from the gate/filter wiring? Configure builds sinks
// with their routing predicates frozen; those predicates must consult mutable
// variables (consent toggles, the optional projection filter) that can change
// after construction.
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
// no line touches disk: fail-safe behaviour reproducing the old posture when
// AppSettings was not ready yet.
//
// Validation sub-directory. Configure(...) accepts a `validationSubdirectory`
// flag for isolated comparison runs. Production boot passes false so the sinks
// write the canonical files directly under
// <telemetryDir>/{app,latency,microphone,corpus}.jsonl.
public static class TelemetryListenerBootstrap
{
    // The dispatcher these sinks were registered on, and the sinks themselves —
    // held so ShutDown can unregister exactly what Configure added (tests, and
    // an eventual host shutdown).
    private static DispatchEventListener? _dispatch;
    private static readonly List<ILogSink> _sinks = new();
    private static bool _configured;

    // External source of truth for user gates. Null = closed posture (every
    // gate returns false). Wired by the App through ConfigureGates; read at
    // every emission so toggle flips in Settings take effect immediately.
    private static Func<string, bool>? _gateReader;
    private static Func<EventEntry, bool>? _applicationLogDropFilter;

    public static void Configure(
        DispatchEventListener dispatch,
        string telemetryDirectory,
        bool validationSubdirectory = true)
    {
        if (dispatch is null) throw new ArgumentNullException(nameof(dispatch));
        if (_configured) return;
        _configured = true;
        _dispatch = dispatch;

        string rootDirectory = validationSubdirectory
            ? Path.Combine(telemetryDirectory, "validation")
            : telemetryDirectory;

        Directory.CreateDirectory(rootDirectory);

        Register(new JsonlSink(
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
            // app.jsonl is the persistent mirror of the live log:
            // self-describing envelope (provider/event/level/source/message/
            // line), rotated by line chunks into numbered generations in
            // archive/ (never renamed or deleted; the user prunes). Datasets
            // stay PayloadOnly without rotation. Rolled-log /
            // untouched-datasets principle: ADR-0007.
            schema:   JsonlSchema.SelfDescribing,
            rotation: new JsonlRotationPolicy(maxLines: 8000)));

        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "latency.jsonl"),
            kindLabel: "latency",
            predicate: e => e.EventName == "LatencyRecorded"
                         && ReadGate("LatencyEnabled")));

        Register(new JsonlSink(
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
        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "microphone.processed.jsonl"),
            kindLabel: "microphone_processed",
            predicate: e => e.EventName == "PreprocessedTelemetryRecorded"
                         && ReadGate("MicrophoneTelemetry")));

        // Normalized corpus: see ADR-0006. Two routed sinks spray
        // CorpusAsr/RewriteRecorded over a bucketed tree. Both predicates gate
        // on CorpusEnabled and the resolver composes the path from the event
        // payload.
        string corpusRoot = Path.Combine(rootDirectory, "corpus");

        Register(new RoutedJsonlSink(
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

        Register(new RoutedJsonlSink(
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

    // Registers a sink on the configured dispatcher and tracks it for ShutDown.
    private static void Register(ILogSink sink)
    {
        _sinks.Add(sink);
        _dispatch!.AddSink(sink);
    }

    // Wires the user gate reader delegate. Accepts a symbolic name
    // ("ApplicationLogToDisk", "LatencyEnabled", "MicrophoneTelemetry",
    // "CorpusEnabled") and returns the current bool. An unknown name must return
    // false on the caller side.
    //
    // Idempotent: calling ConfigureGates again replaces the delegate.
    public static void ConfigureGates(Func<string, bool> gateReader)
    {
        if (gateReader is null) throw new ArgumentNullException(nameof(gateReader));
        _gateReader = gateReader;
    }

    // Wires the entry-level projection filter that removes some events from the
    // persisted application log (e.g. sharing the LogWindow Activity lens). The
    // predicate reads the same EventEntry as the live window, which keeps
    // app.jsonl aligned without introducing a Telemetry → Logging reference.
    // This is app.jsonl-specific; the transverse capture gate lives on the
    // dispatcher, not here.
    public static void ConfigureApplicationLogDropFilter(Func<EventEntry, bool> filter)
    {
        if (filter is null) throw new ArgumentNullException(nameof(filter));
        _applicationLogDropFilter = filter;
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

    // Unregisters every sink Configure added from the dispatcher. Optional —
    // process exit drops the dispatcher anyway — but exposed for tests and an
    // eventual host shutdown sequence.
    public static void ShutDown()
    {
        var dispatch = _dispatch;
        if (dispatch is not null)
            foreach (var sink in _sinks) dispatch.RemoveSink(sink);
        _sinks.Clear();
        _dispatch = null;
        _configured = false;
        _gateReader = null;
        _applicationLogDropFilter = null;
    }
}
