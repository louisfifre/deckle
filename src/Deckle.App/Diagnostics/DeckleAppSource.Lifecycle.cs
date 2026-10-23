using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// The run's story, tagged Milestone so the always-on log file keeps it
// whatever the verbosity controls say: a reader on another machine opens that
// file first and must see a run start, be identified and end.
public sealed partial class DeckleAppSource
{
    [Event(EvtAppStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle starting")]
    public void AppStarting()
    {
        if (IsEnabled()) WriteEvent(EvtAppStarting);
    }

    // Detail of AppStarting: what Windows knows about how this process came to
    // run. LaunchContextProbe says why each field is there.
    [Event(EvtLaunchContextDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "ancestry={0} | elevated={1} | foreground_image={2} | foreground_class={3} | foreground_is_ancestor={4} | foreground_lock_timeout_ms={5}")]
    public void LaunchContextDetail(
        string ancestry, bool elevated,
        string foreground_image, string foreground_class,
        bool foreground_is_ancestor, int foreground_lock_timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtLaunchContextDetail,
            ancestry, elevated, foreground_image, foreground_class,
            foreground_is_ancestor, foreground_lock_timeout_ms);
    }

    // One line that says which Deckle this is, so a log read after the
    // fact is never matched to the wrong build, process or launch mode. The
    // whole identity travels in the payload: a Verbose mirror would stay out
    // of the log.
    [Event(EvtRunIdentity,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle {0} {1} {2} | session={3} | pid={4} | os={5} | supervised={6} | follows_crash={7} | switches={8}")]
    public void RunIdentity(
        string version,
        string stage,
        string build,
        string session,
        int pid,
        string os,
        bool supervised,
        bool follows_crash,
        string switches)
    {
        if (IsEnabled())
            WriteEvent(EvtRunIdentity, version, stage, build, session, pid, os, supervised, follows_crash, switches);
    }

    // What the logs of this run can contain: a reader who finds no
    // transcription detail learns here that the activity was off, not that
    // nothing happened.
    [Event(EvtLoggingPolicy,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Logging policy")]
    public void LoggingPolicy(
        bool ambient,
        bool transcription,
        bool autocorrect,
        bool input,
        bool windowing,
        bool app_log_to_disk)
    {
        if (IsEnabled())
            WriteEvent(EvtLoggingPolicy, ambient, transcription, autocorrect, input, windowing, app_log_to_disk);
    }

    [Event(EvtAppReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Deckle ready")]
    public void AppReady()
    {
        if (IsEnabled()) WriteEvent(EvtAppReady);
    }

    [Event(EvtShutdownCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Shutdown completed")]
    public void ShutdownCompleted(string work_id, int steps, int failed, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtShutdownCompleted, work_id, steps, failed, elapsed_ms);
    }

}
