using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// The composition of the resident app, one line per piece, so the log of
// a run says which modules it had. A skip carries its reason: a missing
// native runtime reads differently from a module the person left out.
public sealed partial class DeckleAppSource
{
    // Written before the module's composition runs: a module that hangs at
    // startup leaves this line with no ModuleStarted after it, so the log
    // names the culprit even when the boot never ends.
    [Event(EvtModuleStarting,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Starting {0}")]
    public void ModuleStarting(string module)
    {
        if (IsEnabled()) WriteEvent(EvtModuleStarting, module);
    }

    [Event(EvtModuleStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Module {0} started")]
    public void ModuleStarted(string module)
    {
        if (IsEnabled()) WriteEvent(EvtModuleStarted, module);
    }

    [Event(EvtModuleSkipped,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Module {0} skipped | reason={1}")]
    public void ModuleSkipped(string module, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtModuleSkipped, module, reason);
    }

    // The cause travels in the payload: this line is the last the log
    // gets from the boot when the exception goes on to end it.
    [Event(EvtModuleFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Module {0} failed to start | {1}: {2}")]
    public void ModuleFailed(string module, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtModuleFailed, module, ex_type, ex_message);
    }

    // The module declined to start without throwing (its start returned
    // false) and the app runs on without it. The module's own provider logs
    // the low-level cause; reason says what the app lost.
    [Event(EvtModuleRefused,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "Module {0} refused to start | reason={1}")]
    public void ModuleRefused(string module, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtModuleRefused, module, reason);
    }
}
