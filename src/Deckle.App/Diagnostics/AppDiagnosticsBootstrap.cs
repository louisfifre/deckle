using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App;

// Boot-time wiring of the EventSource-based observability pipeline. Since the
// dispatch refonte there is exactly ONE EventListener — the
// DispatchEventListener owned here — and every consumer is a passive ILogSink
// registered on it. The dispatcher builds each EventEntry once; sinks only
// route and write. Admission already happened at the producer. See
// Deckle.Diagnostics.ILogSink / DispatchEventListener.
//
// Two phases, with a deliberate order:
//
//   InitializeLocalSinks(diagnosticsDirectory) — called FIRST in
//     App.OnLaunched, before settings migration. Creates the dispatcher and
//     registers the always-on, ungated local sinks (setup.jsonl, errors.jsonl)
//     under diagnostics/. They read no settings, so they can come before
//     everything else — and must, because the dispatcher only receives events
//     emitted AFTER it subscribes, so a failure in the very first boot step
//     (e.g. a settings-migration error) is traced only if the dispatcher and
//     these sinks already exist. Local only, never transmitted — distinct in
//     purpose and folder from telemetry/.
//
//   InitializeOperationalSinks(diagnosticsDirectory) — called after settings
//     migration. Registers diagnostics/app.jsonl and the LogWindow ring buffer.
//
//   InitializeTelemetry(telemetryDirectory) — registers only purpose-specific,
//     consented datasets under telemetry/.
//
// The dispatcher and the LogWindowSink are process-lifetime singletons, held in
// static fields. They are never explicitly disposed — the EventListener
// registration is dropped implicitly at process exit.
internal static class AppDiagnosticsBootstrap
{
    private static DispatchEventListener? _dispatch;
    private static LogWindowSink? _logWindowSink;
    private static bool _hudAttached;

    // App-owned crash detail/stack events (DeckleAppSource). The milestone/
    // Verbose split puts them at Verbose, so a pure Error-level predicate would
    // miss them — the error log allowlists them by name so a crash lands
    // complete (milestone + exception + stack) in errors.jsonl.
    //
    // Matched by bare name, not (provider, name): safe because these names are
    // Deckle-App-exclusive by the one-source-per-module nomenclature — no other
    // Deckle-* provider declares them. If a future provider ever did, scope the
    // companion check by provider (e.Provider == "Deckle-App") so a homonym
    // can't leak into errors.jsonl.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashUnhandledDetail),
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashTaskSchedulerDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    // Returns the process-lifetime dispatcher, creating it on first call. Both
    // boot phases route through here so the single EventListener exists from the
    // earliest registration regardless of call order.
    private static DispatchEventListener Dispatch => _dispatch ??= new DispatchEventListener();

    // Always-on local sinks (setup.jsonl + errors.jsonl). Registered as the
    // FIRST boot step so the riskiest, un-opted-in moments leave a local trace
    // the user owns — see the class header for why ordering is load-bearing.
    // JsonlSink flushes each line synchronously, so a crash keeps its record on
    // disk.
    public static void InitializeLocalSinks(string diagnosticsDirectory)
    {
        // Idempotent: a second call is a no-op. The sinks are process-lifetime
        // singletons; re-registering would double every write and desync the
        // per-instance rotation counters.
        if (_dispatch is not null) return;

        var dispatch = Dispatch;

        // setup.jsonl — the full first-run setup narrative. Keeps EVERY
        // Deckle-Setup event regardless of level, Verbose mirrors included, so
        // the trace carries the whole story (which folder was picked, the exact
        // failure string). Those Verbose mirrors can embed local file paths and
        // the Windows account name (e.g. %LOCALAPPDATA%\Deckle) verbatim — that
        // is intended: the path IS the diagnostic signal, and the file is
        // local-only and user-owned.
        dispatch.AddSink(new JsonlSink(
            filePath:  Path.Combine(diagnosticsDirectory, "setup.jsonl"),
            kindLabel: "setup",
            predicate: static e => e.Kind == ObservationKind.Operational
                                && e.Provider == "Deckle-Setup",
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 5000)));

        // errors.jsonl — every critical error/crash. Keeps Error/Critical by
        // level PLUS the four Verbose crash companions by name (the milestone/
        // Verbose split demotes the crash detail/stack below Error, so a pure
        // level predicate would split a crash across files). Note: an Error-
        // level setup failure also lands here by the level clause, but WITHOUT
        // its Verbose *Detail mirror (not a crash companion) — the cause string
        // lives in setup.jsonl, which is always co-present (registered just
        // above). errors.jsonl is the index of failures; the full setup story
        // stays in setup.jsonl.
        dispatch.AddSink(new JsonlSink(
            filePath:  Path.Combine(diagnosticsDirectory, "errors.jsonl"),
            kindLabel: "error",
            predicate: static e => e.Kind == ObservationKind.Operational
                && (e.Level == EventLevel.Error
                || e.Level == EventLevel.Critical
                || _crashCompanions.Contains(e.EventName)),
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 2000)));
    }

    public static void InitializeOperationalSinks(string diagnosticsDirectory)
    {
        if (_logWindowSink is not null) return;

        var dispatch = Dispatch;
        dispatch.AddSink(new ApplicationLogSink(diagnosticsDirectory));

        // LogWindow sink: starts buffering now. No UI sink is attached at this
        // point; lazy LogWindow will attach via `AttachLogWindowSink` on first
        // open, receive the buffered history as a replay, then stay live.
        _logWindowSink = new LogWindowSink();
        dispatch.AddSink(_logWindowSink);
    }

    public static void InitializeTelemetry(string telemetryDirectory)
    {
        TelemetryListenerBootstrap.Configure(
            Dispatch,
            telemetryDirectory,
            validationSubdirectory: false);
    }

    // Registers the HUD feedback sink once the HUD surfaces are available.
    // Called from App.OnLaunched after the HudWindow and HudOverlayManager have
    // been constructed — the sink can only route events to live UI objects, so
    // deferring registration until they exist avoids a no-op early phase. Module
    // providers that emit UserFeedbackEmitted before this call lose their
    // feedback (no sink registered); in practice that doesn't happen because
    // every UserFeedback emission is in a runtime path (hotkey, rewrite,
    // pairing) that fires well after boot.
    public static void AttachHudFeedbackSink(IHudFeedbackSink sink)
    {
        if (_hudAttached) return;
        _hudAttached = true;
        Dispatch.AddSink(new HudFeedbackSink(sink));
    }

    // Attaches a LogWindow UI sink and replays the buffered history since boot.
    // Called on first LogWindow open (lazy `App.ShowLogWindowLazy` path); the
    // sink receives future events live until `DetachLogWindowSink`.
    public static void AttachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.AttachSink(sink);
    }

    public static void DetachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.DetachSink(sink);
    }

}
