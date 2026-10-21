using System.Diagnostics.Tracing;

namespace Deckle.Diagnostics;

// Cross-cutting sub-provider: the observability pipeline watching itself.
// A sink that throws, a flush that fails or times out, an archive pruned, a
// boot replay that overflowed: each is a fact about the log, not about a
// module, and a reader who finds a hole in a journal needs it beside the hole.
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

    // One incident per sink: opened by the first failure, closed by the next
    // successful write (SinkRecovered). The attempts in between are counted,
    // never emitted.
    [Event(EvtSinkFaulted,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Log sink {0} failed: {1}: {2}")]
    public void SinkFaulted(string sink, string error_type, string error_message)
    {
        if (IsEnabled()) WriteEvent(EvtSinkFaulted, sink, error_type, error_message);
    }

    [Event(EvtSinkRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Log sink {0} writes again after {1} failed writes")]
    public void SinkRecovered(string sink, int failed_writes)
    {
        if (IsEnabled()) WriteEvent(EvtSinkRecovered, sink, failed_writes);
    }

    // Emitted once when boot ends, only if the replay ring dropped entries: a
    // sink registered late then saw a truncated boot history.
    [Event(EvtBootReplayOverflow,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Boot replay buffer dropped {0} events past its capacity of {1}")]
    public void BootReplayOverflow(int dropped, int capacity)
    {
        if (IsEnabled()) WriteEvent(EvtBootReplayOverflow, dropped, capacity);
    }

    [Event(EvtFlushFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Log sink {0} could not flush: {1}: {2}")]
    public void FlushFailed(string sink, string error_type, string error_message)
    {
        if (IsEnabled()) WriteEvent(EvtFlushFailed, sink, error_type, error_message);
    }

    [Event(EvtFlushTimedOut,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Log sink {0} did not flush within {1} ms")]
    public void FlushTimedOut(string sink, int timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtFlushTimedOut, sink, timeout_ms);
    }

    [Event(EvtArchivesPruned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Pruned {0} archived generations of {1}, keeping {2}")]
    public void ArchivesPruned(int pruned, string file_name, int kept)
    {
        if (IsEnabled()) WriteEvent(EvtArchivesPruned, pruned, file_name, kept);
    }
}
