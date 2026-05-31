using Deckle.Diagnostics;
using Deckle.Diagnostics.Listeners;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App.Diagnostics;

// Boot-time wiring of the EventSource-based observability pipeline.
// App.OnLaunched calls Initialize() pour que les listeners soient en
// place avant la première emission de tout provider Deckle.*.
//
// What we wire here:
//   1. JSONL listeners — via TelemetryListenerBootstrap.
//      Écrit sous `<TelemetryDirectory>/{app,latency,microphone,corpus}
//      .jsonl` (paths canoniques depuis la sous-vague 6e — le legacy
//      JsonlFileSink qui les possédait jadis a disparu). Le corpus
//      utilise deux listeners routés pour ASR et rewrite.
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
        // JSONL listeners — écrit aux paths canoniques (`<TelemetryDir>/
        // {app,latency,microphone,corpus}.jsonl`). Le legacy `JsonlFile-
        // Sink` qui les possédait jadis a été retiré en sous-vague 6e,
        // ce pipeline prend la relève sur les mêmes fichiers.
        TelemetryListenerBootstrap.Configure(telemetryDirectory, validationSubdirectory: false);

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

    // Câble un drop filter sur le LogWindowEventListener. Appelée au
    // boot de l'App pour que les Verbose ambient soient filtrés
    // pendant la capture loop quand le toggle utilisateur est off.
    // Le filter est conservé pour la vie du process — un seul filter
    // actif à la fois.
    //
    // Si Initialize n'a pas encore été appelée, l'appel est silencieux
    // (no-op). En pratique l'App appelle Initialize d'abord, puis
    // ConfigureLogWindowDropFilter immédiatement après — l'ordre est
    // strict côté caller.
    public static void ConfigureLogWindowDropFilter(System.Func<EventEntry, bool> filter)
    {
        _logWindowListener?.ConfigureDropFilter(filter);
    }

    public static void ConfigureLogWindowProviderLevelDropFilter(
        System.Func<string, System.Diagnostics.Tracing.EventLevel, bool> filter)
    {
        _logWindowListener?.ConfigureProviderLevelDropFilter(filter);
    }
}
