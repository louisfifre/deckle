using Deckle.Diagnostics;
using Deckle.Diagnostics.Listeners;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.Diagnostics;

// Boot-time wiring of the new EventSource-based observability pipeline,
// installed alongside the legacy TelemetryService sinks during the
// migration window. App.OnLaunched calls Initialize() right after
// TelemetryGates.Configure (legacy gates) and AddSink (legacy file
// sink) so the new listeners are in place before any first emission.
//
// What we wire here:
//   1. JsonlEventListener × 4 — via TelemetryListenerBootstrap.
//      Writes under <TelemetryDirectory>/validation/ during Wave 1
//      to avoid mixing with the legacy file sink that owns the
//      canonical paths.
//   2. LogWindowEventListener — bridge to the legacy LogWindow via
//      LegacyLogWindowSink, so new events show up live next to old
//      ones without LogWindow needing any awareness of EventSource.
//
// The listener instance is held as a static field so it survives for
// the life of the process. EventListener.Dispose() unregisters cleanly;
// process exit handles it implicitly.
internal static class AppDiagnosticsBootstrap
{
    private static LogWindowEventListener? _logWindowListener;

    public static void Initialize(string telemetryDirectory)
    {
        // JSONL listeners — parallel files for schema validation in
        // Wave 1. Flip the second argument to false in Wave 6 to take
        // over the canonical paths once the legacy JsonlFileSink is
        // removed.
        TelemetryListenerBootstrap.Configure(telemetryDirectory, validationSubdirectory: true);

        // LogWindow bridge — wire even before the LogWindow itself
        // exists. The bridge forwards to TelemetryService.Log which
        // buffers in the central history and replays on lazy-open.
        _logWindowListener = new LogWindowEventListener(new LegacyLogWindowSink());
    }
}
