using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics.Listeners;

namespace Deckle.Diagnostics.Telemetry;

// Boot-time configuration of the structured-telemetry JsonlEventListeners.
//
// One static entry point — Configure(...) — wires four listeners
// for the four canonical destinations the legacy JsonlFileSink used
// to route through TelemetryKind:
//
//   app.jsonl        ← milestones (Level <= Informational) excluding
//                      dedicated structured telemetries
//   latency.jsonl    ← LatencyRecorded events
//   microphone.jsonl ← MicrophoneTelemetryRecorded events
//   corpus.jsonl     ← CorpusRecorded events
//
// The listener instances are held by this class for the life of the
// process. EventListener.Dispose() unregisters the listener cleanly;
// callers wanting to drop a destination can use ShutDown() at app
// quit, but in practice the process exit suffices.
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
            predicate: static e =>
                   e.EventName != "LatencyRecorded"
                && e.EventName != "MicrophoneTelemetryRecorded"
                && e.EventName != "CorpusRecorded"));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "latency.jsonl"),
            kindLabel: "latency",
            predicate: static e => e.EventName == "LatencyRecorded"));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "microphone.jsonl"),
            kindLabel: "microphone",
            predicate: static e => e.EventName == "MicrophoneTelemetryRecorded"));

        _listeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(rootDirectory, "corpus.jsonl"),
            kindLabel: "corpus",
            predicate: static e => e.EventName == "CorpusRecorded"));
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
