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
//   - Configure(dispatch, …) builds the canonical dataset JSONL sinks (latency,
//     microphone, processed, then the corpus routes) and registers them on the
//     passed-in dispatcher. Must be called only once at boot.
//   - ConfigureGates(...) wires the delegate that reads user toggles on the
//     host side. May be called before or after Configure; predicates read the
//     last known value at each emission, so a delegate update propagates
//     without rebuilding sinks.
// Why separate Configure from the gate/filter wiring? Configure builds sinks
// with their routing predicates frozen; those predicates must consult mutable
// variables (consent toggles, the optional projection filter) that can change
// after construction.
//
// Canonical destinations:
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
//   autocorrect.decisions.jsonl                    ← Autocorrect{Decision,
//                                                    Rerank}Recorded events
//   autocorrect.text.jsonl                         ← AutocorrectTextRecorded
//                                                    events (typed corpus)
//   autocorrect.stream.jsonl                       ← AutocorrectStreamRecorded
//                                                    events (typing stream)
//
// User gate semantics:
//   latency.jsonl              ← LatencyEnabled == true
//   microphone.jsonl,
//   microphone.processed.jsonl ← MicrophoneTelemetry == true
//   corpus/raw/…,
//   corpus/rewrite-…/      ← CorpusEnabled == true
//   autocorrect.decisions.jsonl ← AutocorrectDecisions == true
//   autocorrect.text.jsonl,
//   autocorrect.stream.jsonl    ← AutocorrectText == true (one consent envelope)
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
            filePath:  Path.Combine(rootDirectory, "latency.jsonl"),
            kindLabel: "latency",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "LatencyRecorded"
                         && ReadGate("LatencyEnabled")));

        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "microphone.jsonl"),
            kindLabel: "microphone",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "MicrophoneTelemetryRecorded"
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
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "PreprocessedTelemetryRecorded"
                         && ReadGate("MicrophoneTelemetry")));

        // Per-word autocorrect decision dataset: every corrected or left-literal
        // word on an enrolled surface with its candidates, scores, margins and the
        // guard that decided it — the diagnostic surface for tuning the corrector.
        // The synchronous decision and its deferred reranker verdict land here,
        // joined by id. Carries typed words by design (see DeckleAutocorrectSource),
        // so it is gated on the dedicated opt-in consent toggle. PayloadOnly,
        // append-only like the other datasets — the words are the record, never
        // rotated.
        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "autocorrect.decisions.jsonl"),
            kindLabel: "autocorrect_decision",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && (e.EventName == "AutocorrectDecisionRecorded"
                          || e.EventName == "AutocorrectRerankRecorded")
                         && ReadGate("AutocorrectDecisions")));

        // Typed-sentence corpus: each sentence typed at the keyboard on an enrolled
        // surface, verbatim form paired with the corrected one — the substrate for
        // modelling the user's own error patterns. Verbatim typed input, the heaviest
        // text capture, so it carries its own opt-in consent toggle, separate from the
        // decision dataset. PayloadOnly, append-only — the text is the record.
        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "autocorrect.text.jsonl"),
            kindLabel: "autocorrect_text",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "AutocorrectTextRecorded"
                         && ReadGate("AutocorrectText")));

        // Typing stream: the verbatim forward flow on enrolled surfaces, one run
        // per line, segmented at backward repairs — replaying the runs restores
        // what was typed, erased and retyped (the mistouch-mining substrate).
        // Same consent envelope as the typed-sentence corpus above (one toggle
        // covers both files), its own kind so the datasets load apart. PayloadOnly,
        // append-only — the text is the record.
        Register(new JsonlSink(
            filePath:  Path.Combine(rootDirectory, "autocorrect.stream.jsonl"),
            kindLabel: "autocorrect_stream",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "AutocorrectStreamRecorded"
                         && ReadGate("AutocorrectText")));

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
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "CorpusAsrRecorded"
                         && ReadGate("CorpusEnabled")));

        Register(new RoutedJsonlSink(
            pathResolver: e =>
            {
                string bucket = e.Payload.TryGetValue("bucket", out var b) ? b?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(bucket)) return "";
                return Path.Combine(corpusRoot, bucket, "corpus.jsonl");
            },
            kindLabel: "corpus_rewrite",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "CorpusRewriteRecorded"
                         && ReadGate("CorpusEnabled")));
    }

    // Registers a sink on the configured dispatcher and tracks it for ShutDown.
    private static void Register(ILogSink sink)
    {
        _sinks.Add(sink);
        _dispatch!.AddSink(sink);
    }

    // Wires the user gate reader delegate. Accepts a symbolic name
    // ("LatencyEnabled", "MicrophoneTelemetry", "CorpusEnabled",
    // "AutocorrectDecisions", "AutocorrectText") and returns the
    // current bool. An unknown name must return false on the caller side.
    //
    // Idempotent: calling ConfigureGates again replaces the delegate.
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
    }
}
