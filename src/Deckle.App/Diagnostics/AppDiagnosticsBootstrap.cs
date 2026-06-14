using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App;

// Boot-time wiring of the EventSource-based observability pipeline, in two
// phases with a deliberate order:
//
//   InitializeLocalSinks(diagnosticsDirectory) — called FIRST in
//     App.OnLaunched, before settings migration. Registers the always-on,
//     ungated local diagnostic sinks (setup.jsonl, errors.jsonl) under
//     diagnostics/. They read no settings, so they can come before everything
//     else — and must, because an EventListener only receives events emitted
//     AFTER it subscribes, so a failure in the very first boot step (e.g. a
//     settings-migration error) is traced only if these sinks already exist.
//     Local only, never transmitted — distinct in purpose and folder from
//     telemetry/.
//
//   InitializeTelemetry(telemetryDirectory) — called later, after settings
//     migration (the telemetry listeners' user gates read the migrated
//     settings). Wires the opt-in JSONL telemetry listeners (via
//     TelemetryListenerBootstrap, canonical paths `<TelemetryDirectory>/
//     {app,latency,microphone,corpus}.jsonl`) and the LogWindowEventListener
//     ring buffer that LogWindow attaches to lazily.
//
// All listeners are process-lifetime singletons, held in static fields so they
// live for the whole run. They are never explicitly disposed — the
// EventListener registration is dropped implicitly at process exit.
internal static class AppDiagnosticsBootstrap
{
    private static LogWindowEventListener? _logWindowListener;
    private static HudFeedbackEventListener? _hudFeedbackListener;

    // Always-on local diagnostic sinks (setup + errors). Held so they live for
    // the process; never explicitly disposed — process exit drops the
    // EventListener registration implicitly, same posture as the JSONL
    // telemetry listeners.
    private static readonly List<EventListener> _diagnosticListeners = new();

    // App-owned crash detail/stack events (DeckleAppSource). The milestone/
    // Verbose split puts them at Verbose, so a pure Error-level predicate would
    // miss them — the error log allowlists them by name so a crash lands
    // complete (milestone + exception + stack) in errors.jsonl.
    //
    // Matched by bare name, not (provider, name): safe because these names are
    // Deckle-App-exclusive by the one-source-per-module nomenclature — no other
    // Deckle-* provider declares them. If a future provider ever did, scope the
    // companion check by provider (e.Provider / d.EventSource.Name == "Deckle-App")
    // so a homonym can't leak into errors.jsonl.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashUnhandledDetail),
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashTaskSchedulerDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    // Always-on local sinks (setup.jsonl + errors.jsonl). Registered as the
    // FIRST boot step so the riskiest, un-opted-in moments leave a local trace
    // the user owns — see the class header for why ordering is load-bearing.
    // JsonlEventListener flushes each line synchronously, so a crash keeps its
    // record on disk. The pre-entry drop predicates reject the bulk of events
    // before an EventEntry is built, sparing the per-frame firehose.
    public static void InitializeLocalSinks(string diagnosticsDirectory)
    {
        // Idempotent: a second call is a no-op. The listeners are process-
        // lifetime singletons; re-registering would double every write and
        // desync the per-instance rotation counters.
        if (_diagnosticListeners.Count > 0) return;

        // setup.jsonl — the full first-run setup narrative. Keeps EVERY
        // Deckle-Setup event regardless of level, Verbose mirrors included, so
        // the trace carries the whole story (which folder was picked, the exact
        // failure string). Those Verbose mirrors can embed local file paths and
        // the Windows account name (e.g. %LOCALAPPDATA%\Deckle) verbatim — that
        // is intended: the path IS the diagnostic signal, and the file is
        // local-only and user-owned. (EventSource.Name is non-null for any
        // enabled Deckle-* source, so the drop predicate needs no null guard,
        // unlike the errors sink below.)
        _diagnosticListeners.Add(new JsonlEventListener(
            filePath:  Path.Combine(diagnosticsDirectory, "setup.jsonl"),
            kindLabel: "setup",
            predicate: static e => e.Provider == "Deckle-Setup",
            preEntryDropPredicate: static d => d.EventSource.Name != "Deckle-Setup",
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
                // EventWrittenEventArgs.EventName is nullable here (unlike the
                // EventEntry.EventName above); a nameless event is never a
                // crash companion, so it stays in the drop set.
                && !(d.EventName is { } eventName && _crashCompanions.Contains(eventName)),
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 2000)));
    }

    // Opt-in telemetry JSONL listeners + the in-memory LogWindow ring buffer.
    // Called later in OnLaunched, after settings migration, because the
    // telemetry listeners' user gates read the migrated settings. The two
    // always-on local sinks are wired separately and earlier — see
    // InitializeLocalSinks.
    public static void InitializeTelemetry(string telemetryDirectory)
    {
        // Idempotent: the LogWindow listener is the single-call sentinel.
        if (_logWindowListener is not null) return;

        // JSONL listeners: write to canonical paths (`<TelemetryDir>/
        // {app,latency,microphone,corpus}.jsonl`). The legacy `JsonlFileSink`
        // that used to own them was removed in sub-wave 6e; this pipeline
        // takes over the same files.
        TelemetryListenerBootstrap.Configure(telemetryDirectory, validationSubdirectory: false);

        // LogWindow listener: starts buffering now. No sink is attached at this
        // point; lazy LogWindow will attach via `AttachLogWindowSink` on first
        // open, receive the buffered history as a replay, then stay live.
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

    // Wires an entry-level drop filter onto LogWindowEventListener (drops by
    // built EventEntry). Available host hook, but NOT wired by App today — the
    // app routes ambient/streaming Verbose silencing through the provider-level
    // filter below (ConfigureLogWindowProviderLevelDropFilter). Kept as the
    // symmetric entry-level counterpart for a host that needs per-entry drops.
    //
    // No-op if InitializeTelemetry has not run yet (_logWindowListener null).
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
