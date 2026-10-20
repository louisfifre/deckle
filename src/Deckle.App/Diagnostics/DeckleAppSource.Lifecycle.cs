using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    [Event(EvtAppStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
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
           Message = "launch context | ancestry={0} | elevated={1} | foreground_image={2} | foreground_class={3} | foreground_is_ancestor={4} | foreground_lock_timeout_ms={5}")]
    public void LaunchContextDetail(
        string ancestry, bool elevated,
        string foreground_image, string foreground_class,
        bool foreground_is_ancestor, int foreground_lock_timeout_ms)
    {
        if (IsEnabled()) WriteEvent(EvtLaunchContextDetail,
            ancestry, elevated, foreground_image, foreground_class,
            foreground_is_ancestor, foreground_lock_timeout_ms);
    }

    [Event(EvtAppReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Deckle ready")]
    public void AppReady()
    {
        if (IsEnabled()) WriteEvent(EvtAppReady);
    }

    [Event(EvtShutdownCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Shutdown completed")]
    public void ShutdownCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownCompleted);
    }

}
