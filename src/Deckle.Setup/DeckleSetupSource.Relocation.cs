using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Data-root relocation ──────────────────────────────────────────────────
    // The move is the whole job of a --relocate-data run: its start, completion
    // and cancel go to the journal; a failure is a Warning, which it keeps.

    [Event(EvtRelocateStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Moving the app data folder")]
    public void RelocateStarted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateStarted);
    }

    [Event(EvtRelocateStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate | from={0} | to={1} | bytes={2}")]
    public void RelocateStartedDetail(string from, string to, long bytes)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateStartedDetail, from, to, bytes);
    }

    [Event(EvtRelocateCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The app data folder moved")]
    public void RelocateCompleted()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCompleted);
    }

    [Event(EvtRelocateCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate done | copied_bytes={0} | files={1} | skipped={2} | duration_ms={3}")]
    public void RelocateCompletedDetail(long copied_bytes, int files, int skipped, long duration_ms)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCompletedDetail, copied_bytes, files, skipped, duration_ms);
    }

    // ex_type is empty for a refusal decided here (insufficient_space,
    // app_still_running); it carries the exception for reason=threw.
    [Event(EvtRelocateFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app data move failed | reason={0} | step={1} | ex_type={2} | ex_message={3}")]
    public void RelocateFailed(string reason, string step, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateFailed, reason, step, ex_type, ex_message);
    }

    [Event(EvtRelocateInsufficientSpaceDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "relocate space short | drive={0} | required_bytes={1} | free_bytes={2}")]
    public void RelocateInsufficientSpaceDetail(string drive, long required_bytes, long free_bytes)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateInsufficientSpaceDetail, drive, required_bytes, free_bytes);
    }

    [Event(EvtRelocateCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "The app data move was cancelled | reason=window_closed | step={0}")]
    public void RelocateCancelled(string step)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCancelled, step);
    }
}
