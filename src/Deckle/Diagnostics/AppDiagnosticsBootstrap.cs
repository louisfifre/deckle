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
//   2. LogWindowEventListener — buffer ring qui démarre vide au boot
//      et accepte des sinks ILogWindowSink lazy. Le LogWindow s'y
//      attache à sa première ouverture via AttachLogWindowSink et
//      reçoit en replay tout l'historique bufferisé depuis le boot.
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
        // JSONL listeners — parallel files for schema validation in
        // Wave 1. Flip the second argument to false in Wave 6 to take
        // over the canonical paths once the legacy JsonlFileSink is
        // removed.
        TelemetryListenerBootstrap.Configure(telemetryDirectory, validationSubdirectory: true);

        // LogWindow listener — démarre le buffer dès le boot. Aucun
        // sink attaché à ce stade ; le LogWindow lazy s'attachera
        // via `AttachLogWindowSink` à sa première ouverture, recevra
        // l'historique buffer en replay, puis sera live ensuite.
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

    // Attache un sink LogWindow et lui rejoue l'historique bufferisé
    // depuis le boot. Appelée à la première ouverture du LogWindow
    // (chemin lazy `App.ShowLogWindowLazy`) ; le sink reçoit live les
    // events futurs jusqu'à `DetachLogWindowSink`.
    public static void AttachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowListener?.AttachSink(sink);
    }

    public static void DetachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowListener?.DetachSink(sink);
    }
}
