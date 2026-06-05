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
//
// The listener instance is held as a static field so it survives for
// the life of the process. EventListener.Dispose() unregisters cleanly;
// process exit handles it implicitly.
internal static class AppDiagnosticsBootstrap
{
    private static LogWindowEventListener? _logWindowListener;
    private static HudFeedbackEventListener? _hudFeedbackListener;

    public static void Initialize(string telemetryDirectory)
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
