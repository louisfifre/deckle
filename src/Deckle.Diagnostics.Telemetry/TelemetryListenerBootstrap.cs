using System;
using System.Collections.Generic;
using System.IO;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Telemetry;

// Boot-time configuration of the dataset JSONL sinks, the dictation corpus:
// passive ILogSinks registered on the host's single DispatchEventListener.
//
// Configure(dispatch, …) builds the sinks once at boot with their routing
// predicates frozen; ConfigureGates(...) wires the delegate that reads the
// user's consent toggles, before or after Configure. The predicates read that
// delegate at each emission, so a toggle flip takes effect without rebuilding
// sinks.
//
// `validationSubdirectory` defaults to true, writing under validation/ for
// isolated comparison runs; production boot passes false so the sinks write
// the canonical files directly under the telemetry directory.
public static class TelemetryListenerBootstrap
{
    // Held so ShutDown can unregister exactly what Configure added.
    private static DispatchEventListener? _dispatch;
    private static readonly List<ILogSink> _sinks = new();
    private static bool _configured;

    // Null until ConfigureGates runs: every gate reads false and no line
    // touches disk.
    private static Func<TelemetrySettings>? _settingsReader;

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

        // The dictation corpus is the only dataset: its ASR and rewrite lines.
        // Every other dataset file (microphone, autocorrect, latency, input
        // recorders) is no longer written, and one already on disk is left
        // where it is.
        string corpusRoot = Path.Combine(rootDirectory, "corpus");

        Register(new RoutedJsonlSink(
            pathResolver: e =>
            {
                // The producer guarantees component presence and sanitization;
                // a malformed payload leaves the path empty, and the sink
                // reports the skipped rows as one incident.
                string bucket = e.Payload.TryGetValue("bucket", out var b) ? b?.ToString() ?? "" : "";
                string tier   = e.Payload.TryGetValue("tier",   out var t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(tier)) return "";
                return Path.Combine(corpusRoot, bucket, tier, "corpus.jsonl");
            },
            kindLabel: "corpus_asr",
            predicate: e => e.Kind == ObservationKind.Dataset
                         && e.EventName == "CorpusAsrRecorded"
                         && ReadGate(s => s.CorpusEnabled)));

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
                         && ReadGate(s => s.CorpusEnabled)));
    }

    private static void Register(ILogSink sink)
    {
        _sinks.Add(sink);
        _dispatch!.AddSink(sink);
    }

    // The reader returns the current TelemetrySettings; each gate picks its own
    // toggle from it, so a renamed toggle fails to compile instead of reading
    // as an unknown name.
    public static void ConfigureGates(Func<TelemetrySettings> settingsReader)
    {
        if (settingsReader is null) throw new ArgumentNullException(nameof(settingsReader));
        _settingsReader = settingsReader;
    }

    private static bool ReadGate(Func<TelemetrySettings, bool> gate)
    {
        var reader = _settingsReader;
        if (reader is null) return false;

        // A reader that throws fails closed all the same: the exception leaves
        // the sink's predicate, so the row is not written, and the dispatcher
        // reports that sink as an incident instead of a consent that silently
        // reads "off".
        return gate(reader());
    }

    // The application-wide ProcessExit barrier handles normal host shutdown;
    // this explicit path serves tests.
    public static void ShutDown()
    {
        var dispatch = _dispatch;
        if (dispatch is not null)
            foreach (var sink in _sinks) dispatch.RemoveSink(sink);

        // Remove first so no new dispatch can enter a completing queue, then
        // deterministically drain everything accepted before the boundary.
        foreach (var sink in _sinks)
        {
            if (sink is not IDisposable disposable) continue;
            // A dataset that cannot drain must not break host shutdown, but
            // the rows it lost are said on the pipeline's own provider.
            try { disposable.Dispose(); }
            catch (Exception ex)
            {
                DeckleDiagnosticsSource.Log.FlushFailed(
                    sink.GetType().Name, ex.GetType().Name, ex.Message);
            }
        }
        _sinks.Clear();
        _dispatch = null;
        _configured = false;
        _settingsReader = null;
    }
}
