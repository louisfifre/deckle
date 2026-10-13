using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App;

// Boot-time wiring of the one EventListener (DispatchEventListener); every
// consumer is a passive ILogSink registered on it, and admission already
// happened at the producer. Phase order is load-bearing: InitializeLocalSinks
// runs before settings migration, the operational sinks after it, and telemetry
// registers only consented datasets under telemetry/. JSONL sinks write through
// bounded queues, so ProcessExit crosses the Flush barrier before termination.
internal static class AppDiagnosticsBootstrap
{
    private static DispatchEventListener? _dispatch;
    private static LogWindowSink? _logWindowSink;
    private static bool _hudAttached;

    // The crash detail and stack events are Verbose, so a level predicate alone
    // would split a crash across files; errors.jsonl allowlists them by name.
    // The bare name holds while no other Deckle-* provider declares these names;
    // scope the check by provider if one ever does.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashUnhandledDetail),
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashTaskSchedulerDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    private static DispatchEventListener Dispatch => _dispatch ??= new DispatchEventListener();

    // A hard crash can still lose entries that had not left the bounded writer
    // queue; explicit exits cross the Flush barrier.
    public static void InitializeLocalSinks(string diagnosticsDirectory)
    {
        // Returns once the dispatcher exists, so this must run before any other
        // phase. Registering twice would double every write and desync the
        // per-instance rotation counters.
        if (_dispatch is not null) return;

        var dispatch = Dispatch;

        // setup.jsonl keeps every Deckle-Setup event, Verbose mirrors included.
        // Those can embed local paths and the Windows account name verbatim:
        // intended, the path is the diagnostic signal and the file stays local.
        dispatch.AddSink(new JsonlSink(
            filePath:  Path.Combine(diagnosticsDirectory, "setup.jsonl"),
            kindLabel: "setup",
            predicate: static e => e.Kind == ObservationKind.Operational
                                && e.Provider == "Deckle-Setup",
            schema:    JsonlSchema.SelfDescribing,
            rotation:  new JsonlRotationPolicy(maxLines: 5000)));

        // An Error-level setup failure lands here without its Verbose detail
        // mirror; the cause string lives in setup.jsonl.
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

        // Buffers from boot: the lazy LogWindow attaches on first open and
        // receives the buffer as a replay.
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

    // UserFeedbackEmitted before this call is dropped: the sink needs the live
    // HUD surfaces, so it registers once they exist.
    public static void AttachHudFeedbackSink(IHudFeedbackSink sink)
    {
        if (_hudAttached) return;
        _hudAttached = true;
        Dispatch.AddSink(new HudFeedbackSink(sink));
    }

    public static void AttachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.AttachSink(sink);
    }

    public static void DetachLogWindowSink(ILogWindowSink sink)
    {
        _logWindowSink?.DetachSink(sink);
    }

    public static void ClearLogWindowHistory()
    {
        _logWindowSink?.ClearBuffer();
    }

    // Every background JSONL writer drains the entries accepted before this call.
    public static bool Flush() => _dispatch?.FlushSinks() ?? true;

}
