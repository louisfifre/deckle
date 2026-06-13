using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Listeners;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App.Diagnostics;

// Boot-time wiring of the EventSource-based observability pipeline.
// App.OnLaunched calls Initialize() so listeners are in place before the
// first emission from any Deckle.* provider.
//
// What we wire here:
//   1. JSONL listeners — via TelemetryListenerBootstrap.
//      Writes under `<TelemetryDirectory>/{app,latency,microphone,corpus}
//      .jsonl` (canonical paths since sub-wave 6e; the legacy JsonlFileSink
//      that used to own them is gone). The corpus uses two routed listeners
//      for ASR and rewrite.
//   2. LogWindowEventListener — ring buffer that starts empty at boot and
//      accepts lazy ILogWindowSink sinks. LogWindow attaches to it on first
//      open via AttachLogWindowSink and receives a replay of the full
//      buffered history since boot.
//   3. Local diagnostic sinks (setup.jsonl, errors.jsonl) — always-on,
//      ungated JSONL listeners under diagnostics/. They capture the first-run
//      setup narrative and every critical error/crash, so the moments before
//      the user opts into telemetry still leave a local trace. Local only,
//      never transmitted — distinct in purpose and folder from telemetry/.
//
// The listener instance is held as a static field so it survives for
// the life of the process. EventListener.Dispose() unregisters cleanly;
// process exit handles it implicitly.
internal static class AppDiagnosticsBootstrap
{
    private static LogWindowEventListener? _logWindowListener;
    private static HudFeedbackEventListener? _hudFeedbackListener;

    // Always-on local diagnostic sinks (setup + errors). Held so they live for
    // the process; EventListener.Dispose unregisters cleanly and process exit
    // handles it implicitly — same posture as the JSONL telemetry listeners.
    private static readonly List<EventListener> _diagnosticListeners = new();

    // App-owned crash detail/stack events (DeckleAppSource). The milestone/
    // Verbose split puts them at Verbose, so a pure Error-level predicate would
    // miss them — the error log allowlists them by name so a crash lands
    // complete (milestone + exception + stack) in errors.jsonl.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashUnhandledDetail),
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashTaskSchedulerDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    public static void Initialize(string telemetryDirectory, string diagnosticsDirectory)
    {
        // JSONL listeners: write to canonical paths (`<TelemetryDir>/
        // {app,latency,microphone,corpus}.jsonl`). The legacy `JsonlFileSink`
        // that used to own them was removed in sub-wave 6e; this pipeline
        // takes over the same files.
        TelemetryListenerBootstrap.Configure(telemetryDirectory, validationSubdirectory: false);

        // LogWindow listener: starts buffering at boot. No sink is attached at
        // this point; lazy LogWindow will attach via `AttachLogWindowSink` on
        // first open, receive the buffered history as a replay, then stay live.
        _logWindowListener = new LogWindowEventListener();

        // Local diagnostic sinks — always on, ungated, written to the dedicated
        // diagnostics/ folder (never telemetry/). They exist so the riskiest,
        // un-opted-in moments leave a trace the user owns: the first-run setup
        // narrative and every critical error/crash. Local only, never
        // transmitted — distinct in purpose from the opt-in telemetry streams.
        // JsonlEventListener flushes each line synchronously, so a crash keeps
        // its record on disk. The pre-entry drop predicates reject the bulk of
        // events before an EventEntry is built, sparing the per-frame firehose.
        _diagnosticListeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(diagnosticsDirectory, "setup.jsonl"),
            kindLabel: "setup",
            predicate: static e => e.Provider == "Deckle-Setup",
            preEntryDropPredicate: static d => d.EventSource.Name != "Deckle-Setup",
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 5000)));

        _diagnosticListeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(diagnosticsDirectory, "errors.jsonl"),
            kindLabel: "error",
            predicate: static e =>
                   e.Level == EventLevel.Error
                || e.Level == EventLevel.Critical
                || _crashCompanions.Contains(e.EventName),
            preEntryDropPredicate: static d =>
                   d.Level != EventLevel.Error
                && d.Level != EventLevel.Critical
                && !_crashCompanions.Contains(d.EventName),
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 2000)));
    }

    // Wires the HudFeedbackEventListener once the HUD surfaces are
    // available. Called from App.OnLaunched after the HudWindow and
    // HudOverlayManager have been constructed — the listener can only
    // route events to live UI objects, so deferring construction until
    // they exist avoids a no-op early phase. Module providers that
    // emit UserFeedbackEmitted before this call lose their feedback
    // (no listener attached); in practice that doesn't happen because
    // every UserFeedback emission is in a runtime path (hotkey,
    // rewrite, pairing) that fires well after boot.
    public static void AttachHudFeedbackSink(IHudFeedbackSink sink)
    {
        _hudFeedbackListener = new HudFeedbackEventListener(sink);
    }

    // Attaches a LogWindow sink and replays the buffered history since boot.
    // Called on first LogWindow open (lazy `App.ShowLogWindowLazy` path); the
    // sink receives future events live until `DetachLogWindowSink`.
    public static void AttachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowListener?.AttachSink(sink);
    }

    public static void DetachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowListener?.DetachSink(sink);
    }

    // Wires a drop filter onto LogWindowEventListener. Called at App boot so
    // ambient Verbose events are filtered during the capture loop when the
    // user toggle is off. The filter is kept for the process lifetime; only
    // one filter is active at a time.
    //
    // If Initialize has not been called yet, the call is silent (no-op). In
    // practice App calls Initialize first, then ConfigureLogWindowDropFilter
    // immediately after; the order is strict on the caller side.
    public static void ConfigureLogWindowDropFilter(System.Func<EventEntry, bool> filter)
    {
        _logWindowListener?.ConfigureDropFilter(filter);
    }

    public static void ConfigureLogWindowProviderLevelDropFilter(
        System.Func<string, System.Diagnostics.Tracing.EventLevel, System.Diagnostics.Tracing.EventKeywords, bool> filter)
    {
        _logWindowListener?.ConfigureProviderLevelDropFilter(filter);
    }
}
