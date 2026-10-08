using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Orchestration;

[EventSource(Name = "Deckle-Orchestration")]
public sealed class DeckleOrchestrationSource : DeckleEventSource
{
    public static readonly DeckleOrchestrationSource Log = new();

    private DeckleOrchestrationSource() { }

    public const int EvtSettingsLoadWarning = 1;
    public const int EvtSettingsLoadError = 2;
    public const int EvtEntryAdded = 3;
    public const int EvtEntryAddedDetail = 4;
    public const int EvtEntryRemoved = 5;
    public const int EvtEntryRemovedDetail = 6;
    public const int EvtPageActionFailed = 7;
    public const int EvtPageActionFailedDetail = 8;

    [Event(EvtSettingsLoadWarning,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadWarning(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadWarning, message);
    }

    [Event(EvtSettingsLoadError,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "{0}")]
    public void SettingsLoadError(string message)
    {
        if (IsEnabled()) WriteEvent(EvtSettingsLoadError, message);
    }

    [Event(EvtEntryAdded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app was added to the registry")]
    public void EntryAdded()
    {
        if (IsEnabled()) WriteEvent(EvtEntryAdded);
    }

    [Event(EvtEntryAddedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "registry entry added | entry_id={0} | display_name={1} | source={2} | process_name={3}")]
    public void EntryAddedDetail(string entry_id, string display_name, string source, string process_name)
    {
        if (IsEnabled()) WriteEvent(EvtEntryAddedDetail, entry_id, display_name, source, process_name);
    }

    [Event(EvtEntryRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An app was removed from the registry")]
    public void EntryRemoved()
    {
        if (IsEnabled()) WriteEvent(EvtEntryRemoved);
    }

    [Event(EvtEntryRemovedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "registry entry removed | entry_id={0}")]
    public void EntryRemovedDetail(string entry_id)
    {
        if (IsEnabled()) WriteEvent(EvtEntryRemovedDetail, entry_id);
    }

    // A picker or dialog on the settings page threw. Caught so the page stays
    // usable; the Verbose mirror names the action and the exception.
    [Event(EvtPageActionFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An action on the apps and games page failed")]
    public void PageActionFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPageActionFailed);
    }

    [Event(EvtPageActionFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page action failed | action={0} | error_type={1} | message={2}")]
    public void PageActionFailedDetail(string action, string error_type, string message)
    {
        if (IsEnabled()) WriteEvent(EvtPageActionFailedDetail, action, error_type, message);
    }
}
