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
