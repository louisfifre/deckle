using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// The run's story, tagged Journal so the always-on journal file keeps it
// whatever the verbosity controls say: a reader on another machine opens that
// file first and must see a run start, be identified and end.
public sealed partial class DeckleAppSource
{
    [Event(EvtAppStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Deckle starting")]
    public void AppStarting()
    {
        if (IsEnabled()) WriteEvent(EvtAppStarting);
    }

    [Event(EvtAppReady,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Deckle ready")]
    public void AppReady()
    {
        if (IsEnabled()) WriteEvent(EvtAppReady);
    }

    [Event(EvtShutdownCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Shutdown completed")]
    public void ShutdownCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtShutdownCompleted);
    }

}
