using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Text;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Diagnostics.Telemetry;

namespace Deckle.App;

// Boot-time wiring of the one EventListener (DispatchEventListener); every
// consumer is a passive ILogSink registered on it, and admission already
// happened at the producer. Phase order is load-bearing: InitializeLocalSinks
// runs before settings migration, the operational sinks after it, and telemetry
// registers only consented datasets under telemetry/. The listener keeps the
// boot history until CompleteBoot, so a sink of a later phase still starts at
// AppStarting. JSONL sinks write through bounded queues, so every exit crosses
// a Flush barrier before termination.
internal static class AppDiagnosticsBootstrap
{
    // Written when a flush could not drain at exit or at a crash: the log
    // itself may be the sink that failed, so the fact lives in its own file.
    private const string FlushFailureFileName = "flush-failed.txt";

    private static DispatchEventListener? _dispatch;
    private static LogWindowSink? _logWindowSink;
    private static string? _diagnosticsDirectory;
    private static bool _hudAttached;

    // What InitializeLocalSinks found of the legacy log file names.
    public static LegacyFileRename? LegacyFileRename { get; private set; }

    // The crash detail and stack events are Verbose, so a level predicate alone
    // would split a crash across files; the log allowlists them by name.
    // The bare name holds while no other Deckle-* provider declares these names;
    // scope the check by provider if one ever does.
    private static readonly HashSet<string> _crashCompanions = new()
    {
        nameof(DeckleAppSource.CrashAppDomainDetail),
        nameof(DeckleAppSource.CrashStackTrace),
    };

    private static DispatchEventListener Dispatch => _dispatch ??= new DispatchEventListener();

    // A hard crash can still lose entries that had not left the bounded writer
    // queue; explicit exits and the crash handlers cross the Flush barrier.
    public static void InitializeLocalSinks(string diagnosticsDirectory)
    {
        // Returns once the dispatcher exists, so this must run before any other
        // phase. Registering twice would double every write and desync the
        // per-instance rotation counters.
        if (_dispatch is not null) return;

        _diagnosticsDirectory = diagnosticsDirectory;

        // Before any sink opens its file: the log and the trace still carry
        // their pre-v0.69 names on an older install. No sink exists yet to say
        // how it went, so the host reports the result after AppStarting.
        LegacyFileRename = DiagnosticsFiles.RenameLegacyFiles(diagnosticsDirectory);

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
            // Three generations: the updater's history matters for one or two
            // runs back, never for months.
            rotation:  new JsonlRotationPolicy(maxLines: 5000, maxArchives: 3)));

        // Warning and above always pass the producer-side admission, so this
        // file needs no setting. A milestone's cause travels in its own payload
        // (deckle-logging): a Verbose mirror gated off never reaches here.
        dispatch.AddSink(new JsonlSink(
            filePath:  Path.Combine(diagnosticsDirectory, DiagnosticsFiles.Log),
            kindLabel: "log",
            predicate: static e => e.Kind == ObservationKind.Operational
                && (e.Level <= EventLevel.Warning
                || e.Milestone
                || _crashCompanions.Contains(e.EventName)),
            schema:    JsonlSchema.SelfDescribing,
            // Five generations: the log is sparse, so this spans weeks of
            // runs, and an incident older than that has lost its context anyway.
            rotation:  new JsonlRotationPolicy(maxLines: 4000, maxArchives: 5)));
    }

    public static void InitializeOperationalSinks(string diagnosticsDirectory)
    {
        if (_logWindowSink is not null) return;

        var dispatch = Dispatch;
        dispatch.AddSink(new ApplicationLogSink(diagnosticsDirectory), replayBoot: true);

        // Buffers from boot: the lazy LogWindow attaches on first open and
        // receives the buffer as a replay.
        _logWindowSink = new LogWindowSink();
        dispatch.AddSink(_logWindowSink, replayBoot: true);
    }

    public static void InitializeTelemetry(string telemetryDirectory)
    {
        TelemetryListenerBootstrap.Configure(
            Dispatch,
            telemetryDirectory,
            validationSubdirectory: false);
    }

    // After the last phase-registered sink: the boot history is released and
    // any sink added later (the HUD feedback sink) starts from its attach.
    public static void CompleteBoot() => _dispatch?.CompleteBoot();

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

    // Every background JSONL writer drains the entries accepted before this
    // call, or gives up at the deadline. True when every destination drained.
    // A failure is also written to its own marker file, since the failing
    // destination may be the one that would have recorded it.
    public static bool Flush(TimeSpan timeout)
    {
        if (_dispatch is null) return true;

        IReadOnlyList<SinkFlushFailure> failures = _dispatch.FlushSinks(timeout);
        if (failures.Count == 0) return true;

        RecordFlushFailure(failures, timeout);
        return false;
    }

    private static void RecordFlushFailure(IReadOnlyList<SinkFlushFailure> failures, TimeSpan timeout)
    {
        string? directory = _diagnosticsDirectory;
        if (directory is null) return;

        try
        {
            var text = new StringBuilder();
            text.Append(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture))
                .Append(" session=").Append(DeckleEventSource.SessionId)
                .Append(" timeout_ms=").Append(((int)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                .AppendLine();
            foreach (SinkFlushFailure failure in failures)
            {
                text.Append("  ").Append(failure.Sink).Append(": ")
                    .AppendLine(failure.Error is null
                        ? "timed out with entries still queued"
                        : $"{failure.Error.GetType().Name}: {failure.Error.Message}");
            }
            File.AppendAllText(Path.Combine(directory, FlushFailureFileName), text.ToString());
        }
        catch
        {
            // Nothing left to tell it to.
        }
    }
}
