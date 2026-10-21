using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// The composition of the resident app, one line per piece, so the journal of
// a run says which modules it had. A skip carries its reason: a missing
// native runtime reads differently from a module the person left out.
public sealed partial class DeckleAppSource
{
    [Event(EvtModuleStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Module {0} started")]
    public void ModuleStarted(string module)
    {
        if (IsEnabled()) WriteEvent(EvtModuleStarted, module);
    }

    [Event(EvtModuleSkipped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Module {0} skipped | reason={1}")]
    public void ModuleSkipped(string module, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtModuleSkipped, module, reason);
    }

    // The cause travels in the payload: this line is the last the journal
    // gets from the boot when the exception goes on to end it.
    [Event(EvtModuleFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Module {0} failed to start | {1}: {2}")]
    public void ModuleFailed(string module, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFailed, module, ex_type, ex_message);
    }
}
