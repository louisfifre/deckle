using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: the observability pipeline watching itself.
// A sink that throws, a flush that fails or times out, an archive pruned, a
// boot replay that overflowed: each is a fact about the log, not about a
// module, and a reader who finds a hole in a log needs it beside the hole.
//
// Every event here carries its cause in its own payload: this provider is
// the one place where a Verbose mirror cannot be trusted to reach any file.
//
// Re-entrancy. DispatchEventListener emits these from inside its own fan-out,
// so an event here dispatches to the same sinks, the faulted one included.
// The listener guards that recursion; this provider stays a plain emitter.
[EventSource(Name = "Deckle-Diagnostics")]
public sealed class DeckleDiagnosticsSource : DeckleEventSource
{
    public static readonly DeckleDiagnosticsSource Log = new();

    private DeckleDiagnosticsSource() { }

    // IDs are public in the ETW manifest: never reuse one, retired ids included.
    public const int EvtSinkFaulted        = 1;
    public const int EvtSinkRecovered      = 2;
    public const int EvtBootReplayOverflow = 3;
    public const int EvtFlushFailed        = 4;
    public const int EvtFlushTimedOut      = 5;
    public const int EvtArchivesPruned     = 6;
    public const int EvtRotationFailed     = 7;
    public const int EvtRotationRecovered  = 8;
    public const int EvtRowUnrouted        = 9;
    public const int EvtRowsRoutedAgain    = 10;
    public const int EvtMessageTemplateBroken = 11;
    public const int EvtSettingsLoadWarning = 12;
    public const int EvtSettingsLoadError = 13;

    // One incident per sink: opened by the first failure, closed by the next
    // successful write (SinkRecovered). The attempts in between are counted,
    // never emitted. SinkRecovered is Milestone-tagged: the log keeps the
    // failure, so it keeps its end too.
    [Event(EvtSinkFaulted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log sink failed to write\nsink={0} | error_type={1} | error_message={2}")]
    public void SinkFaulted(string sink, string error_type, string error_message)
    {
        if (IsEnabled()) WriteEvent(EvtSinkFaulted, sink, error_type, error_message);
    }

    [Event(EvtSinkRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A log sink writes again")]
    public void SinkRecovered(string sink, int failed_writes)
    {
        if (IsEnabled()) WriteEvent(EvtSinkRecovered, sink, failed_writes);
    }

    // Emitted once when boot ends, only if the replay ring dropped entries: a
    // sink registered late then saw a truncated boot history.
    [Event(EvtBootReplayOverflow,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The boot replay buffer was full, so early events were dropped\ndropped={0} | capacity={1}")]
    public void BootReplayOverflow(int dropped, int capacity)
    {
        if (IsEnabled()) WriteEvent(EvtBootReplayOverflow, dropped, capacity);
    }

    [Event(EvtFlushFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log sink could not flush\nsink={0} | error_type={1} | error_message={2}")]
    public void FlushFailed(string sink, string error_type, string error_message)
    {
        if (IsEnabled()) WriteEvent(EvtFlushFailed, sink, error_type, error_message);
    }

    [Event(EvtFlushTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log sink did not flush in time\nsink={0} | timeout_ms={1}")]
    public void FlushTimedOut(string sink, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtFlushTimedOut, sink, timeout_ms);
    }

    [Event(EvtArchivesPruned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Old log archives were pruned")]
    public void ArchivesPruned(int pruned, string file_name, int kept)
    {
        if (IsEnabled()) WriteEvent(EvtArchivesPruned, pruned, file_name, kept);
    }

    // The file keeps taking rows, so this is a degradation: it grows past its
    // cap, or its archive keeps old generations, until the cause goes away.
    // One incident per sink, like SinkFaulted: the first failure is reported,
    // the ones after it are counted, and RotationRecovered closes it. step is
    // roll (the active file could not move into the archive), prune (an old
    // generation could not be deleted) or count_lines (the active file could
    // not be read, so it is taken for empty).
    [Event(EvtRotationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log file could not be rolled or pruned\nfile_name={0} | step={1} | error_type={2} | error_message={3}")]
    public void RotationFailed(string file_name, string step, string error_type, string error_message)
    {
        if (IsEnabled()) WriteEvent(EvtRotationFailed, file_name, step, error_type, error_message);
    }

    // Milestone-tagged: the log keeps the failure, so it keeps its end too.
    [Event(EvtRotationRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A log file rolls and prunes again")]
    public void RotationRecovered(string file_name, int failed_attempts)
    {
        if (IsEnabled()) WriteEvent(EvtRotationRecovered, file_name, failed_attempts);
    }

    // A routed dataset row whose path resolver returned nothing is skipped: the
    // dataset has a hole. One incident per sink, closed by the first row that
    // routes again, which carries the count of rows lost in between.
    [Event(EvtRowUnrouted,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A log sink skipped a row because it had no path for it\nsink={0}")]
    public void RowUnrouted(string sink)
    {
        if (IsEnabled()) WriteEvent(EvtRowUnrouted, sink);
    }

    [Event(EvtRowsRoutedAgain,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A log sink routes rows again")]
    public void RowsRoutedAgain(string sink, int skipped_rows)
    {
        if (IsEnabled()) WriteEvent(EvtRowsRoutedAgain, sink, skipped_rows);
    }

    // An event whose Message template cannot be formatted from its payload (a
    // placeholder with no argument, a malformed brace). Its rows still reach
    // every sink with their payload, only the rendered sentence is missing, so
    // this is a degradation. Reported once per provider and event: the template
    // is fixed in code, so every emission would fail the same way.
    [Event(EvtMessageTemplateBroken,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An event's message template cannot be formatted\nevent_name={1} | provider={0} | error_type={2}")]
    public void MessageTemplateBroken(string provider, string event_name, string error_type)
    {
        if (IsEnabled()) WriteEvent(EvtMessageTemplateBroken, provider, event_name, error_type);
    }

    // The settings store reports in free text; the text carries its own cause.
    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The logging and telemetry settings store worked around a problem\ndetail={0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The logging and telemetry settings could not be saved or applied\ndetail={0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }
}
