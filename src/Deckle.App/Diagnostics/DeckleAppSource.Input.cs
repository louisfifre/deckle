using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // A module that consumes the shared Raw Input host could not run while
    // its switch says on. Deckle-Input logs the host's own cause; this line
    // names the consumer the person loses. consumer: precision-scroll,
    // trackpad, mouse-wheel-recorder, paragraph-rewrite. reason:
    // host_start_failed or engine_start_failed.
    [Event(EvtInputConsumerUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An input module could not start\nconsumer={0} | reason={1}")]
    public void InputConsumerUnavailable(string consumer, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtInputConsumerUnavailable, consumer, reason);
    }
}
