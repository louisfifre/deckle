using System.Globalization;
using System.Diagnostics.Tracing;
using System.Text;
using Deckle.Diagnostics;

namespace Deckle.Hosting;

// Boot-time wiring of the one EventListener (DispatchEventListener) of a
// process; every consumer is a passive ILogSink registered on it, and
// admission already happened at the producer. The executable adds its own
// sinks (a log window, a HUD feedback line) on Dispatch; what every app of the
// suite shares lives here: the local setup and log files, the boot release and
// the flush barrier.
//
// The diagnostics folder is the one AppPaths resolves for the app's identity
// (the suite's shared folder for the published Deckle, one subfolder of it for
// each other app), so this class takes it as a parameter and never reads an
// identity: two apps never write the same file.
//
// Phase order is load-bearing: InitializeLocalSinks runs before the settings
// load, the operational sinks after it. The listener keeps the boot history
// until CompleteBoot, so a sink of a later phase still starts at AppStarting.
// JSONL sinks write through bounded queues, so every exit crosses a Flush
// barrier before termination.
public static class DiagnosticsHost
{
    // Written when a flush could not drain at exit or at a crash: the log
    // itself may be the sink that failed, so the fact lives in its own file.
    private const string FlushFailureFileName = "flush-failed.txt";

    private static DispatchEventListener? _dispatch;
    private static string? _diagnosticsDirectory;

    public static DispatchEventListener Dispatch => _dispatch ??= new DispatchEventListener();

    // A hard crash can still lose entries that had not left the bounded writer
    // queue; explicit exits and the crash handlers cross the Flush barrier.
    //
    // crashCompanions: the crash detail and stack events are Verbose, so a
    // level predicate alone would split a crash across files; the log
    // allowlists them by name. The bare name holds while no other Deckle-*
    // provider declares these names; scope the check by provider if one ever
    // does.
    public static void InitializeLocalSinks(string diagnosticsDirectory, IReadOnlySet<string> crashCompanions)
    {
        // Returns once the dispatcher exists, so this must run before any other
        // phase. Registering twice would double every write and desync the
        // per-instance rotation counters.
        if (_dispatch is not null) return;

        _diagnosticsDirectory = diagnosticsDirectory;

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
            predicate: e => e.Kind == ObservationKind.Operational
                && (e.Level <= EventLevel.Warning
                || e.Milestone
                || e.Summary
                || crashCompanions.Contains(e.EventName)),
            schema:    JsonlSchema.SelfDescribing,
            // Twenty generations: summaries (heartbeats, rollups, a take's
            // figures) fill the file faster than milestones alone did, so it
            // keeps more archives to still span days of runs, and an incident
            // older than that has lost its context anyway.
            rotation:  new JsonlRotationPolicy(maxLines: 4000, maxArchives: 20)));
    }

    // After the last phase-registered sink: the boot history is released and
    // any sink added later starts from its attach.
    public static void CompleteBoot() => _dispatch?.CompleteBoot();

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
