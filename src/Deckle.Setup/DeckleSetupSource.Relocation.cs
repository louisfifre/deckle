using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Data-root relocation ──────────────────────────────────────────────────
    // The move is the whole job of a --relocate-data run: its start, completion
    // and cancel go to the log, and a failure is an Error, which the log
    // keeps: the folder stays where it was and the run stops. All four carry the
    // work_id of one attempt (a retry is a new attempt), so a start with no end
    // reads as abandoned.

    [Event(EvtRelocateStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Moving the app data folder | work_id={0}")]
    public void RelocateStarted(string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateStarted, work_id);
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
           Tags = ObservationTags.Milestone,
           Message = "The app data folder moved | work_id={0}")]
    public void RelocateCompleted(string work_id)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCompleted, work_id);
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

    // Diagnostics files the live app still held were left behind: the move
    // finished, but not whole. One line with the count, whatever the number of
    // files, written before the completion it qualifies. Required data is never
    // skipped; that stops the move with an Error instead.
    [Event(EvtRelocateFilesSkipped,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app data move left diagnostics files behind | work_id={0} | skipped={1}")]
    public void RelocateFilesSkipped(string work_id, int skipped)
    {
        if (IsEnabled(EventLevel.Warning, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateFilesSkipped, work_id, skipped);
    }

    // ex_type is empty for a refusal decided here (insufficient_space,
    // app_still_running); it carries the exception for reason=threw.
    [Event(EvtRelocateFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The app data move failed | work_id={0} | reason={1} | step={2} | ex_type={3} | ex_message={4}")]
    public void RelocateFailed(string work_id, string reason, string step, string ex_type, string ex_message)
    {
        if (IsEnabled(EventLevel.Error, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateFailed, work_id, reason, step, ex_type, ex_message);
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
           Tags = ObservationTags.Milestone,
           Message = "The app data move was cancelled | work_id={0} | reason=window_closed | step={1}")]
    public void RelocateCancelled(string work_id, string step)
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtRelocateCancelled, work_id, step);
    }
}
