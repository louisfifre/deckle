using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtCrashDumpCaptureArmed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Crash dump capture is armed")]
    public void CrashDumpCaptureArmed()
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpCaptureArmed);
    }

    [Event(EvtCrashDumpCaptureArmedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash dump capture | folder={0} | type={1} | retained={2}")]
    public void CrashDumpCaptureArmedDetail(string folder, string dump_type, int retained)
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpCaptureArmedDetail, folder, dump_type, retained);
    }

    [Event(EvtCrashDumpCaptureOff,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Crash dump capture is off — a fatal runtime error will leave no dump")]
    public void CrashDumpCaptureOff()
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpCaptureOff);
    }

    [Event(EvtCrashDumpCaptureOffDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash dump capture off | {0}={1}")]
    public void CrashDumpCaptureOffDetail(string variable, string value)
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpCaptureOffDetail, variable, value);
    }

    [Event(EvtCrashDumpFound,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A crash dump from an earlier run is waiting")]
    public void CrashDumpFound()
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpFound);
    }

    [Event(EvtCrashDumpFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash dump | path={0} | size_bytes={1} | written_at={2}")]
    public void CrashDumpFoundDetail(string path, long size_bytes, string written_at)
    {
        if (IsEnabled()) WriteEvent(EvtCrashDumpFoundDetail, path, size_bytes, written_at);
    }

    [Event(EvtCrashRelaunched,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Deckle was relaunched after it crashed")]
    public void CrashRelaunched()
    {
        if (IsEnabled()) WriteEvent(EvtCrashRelaunched);
    }

    [Event(EvtCrashRelaunchedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "crash relaunch | previous_exit_code={0} | relaunch={1} | max_relaunches={2}")]
    public void CrashRelaunchedDetail(string previous_exit_code, int relaunch, int max_relaunches)
    {
        if (IsEnabled()) WriteEvent(EvtCrashRelaunchedDetail, previous_exit_code, relaunch, max_relaunches);
    }
}
