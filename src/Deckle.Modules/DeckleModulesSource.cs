using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Modules;

// Covers catalogue registration (ModuleRegistry) and the presence choice's
// load/save lifecycle (ModulePresence / PresenceFile).
[EventSource(Name = "Deckle-Modules")]
public sealed class DeckleModulesSource : DeckleEventSource
{
    public static readonly DeckleModulesSource Log = new();

    private DeckleModulesSource() { }

    // ── Catalogue ──
    public const int EvtModuleRegistered          = 1;

    // ── Presence choice lifecycle ──
    public const int EvtPresenceLoaded            = 2;
    public const int EvtPresenceLoadedDetail      = 3;
    public const int EvtPresenceSaved             = 4;
    public const int EvtPresenceSavedDetail       = 5;
    public const int EvtPresenceLoadFailed        = 6;
    // 7 retired.

    // Plumbing detail with ids ⇒ Verbose, no milestone (it fires once per
    // module at every boot).
    [Event(EvtModuleRegistered,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module registered | id={0} | order={1} | deps={2}")]
    public void ModuleRegistered(string id, int order, string deps)
    {
        if (IsEnabled()) WriteEvent(EvtModuleRegistered, id, order, deps);
    }

    [Event(EvtPresenceLoaded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Module presence loaded")]
    public void PresenceLoaded()
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoaded);
    }

    [Event(EvtPresenceLoadedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module presence loaded | mode={0} | present={1} | known={2}")]
    public void PresenceLoadedDetail(string mode, string present, string known)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoadedDetail, mode, present, known);
    }

    [Event(EvtPresenceSaved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The module selection was saved")]
    public void PresenceSaved()
    {
        if (IsEnabled()) WriteEvent(EvtPresenceSaved);
    }

    [Event(EvtPresenceSavedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "module selection saved | present={0} | known={1}")]
    public void PresenceSavedDetail(string present, string known)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceSavedDetail, present, known);
    }

    // The presence file could not be used, so every module counts as present:
    // the Warning names what was asked (the file) and what was obtained. reason
    // is an exception type or a value of PresenceLoadFailureReasons; neither the
    // exception message nor the path is written, since framework messages quote
    // the absolute path under the user's profile.
    [Event(EvtPresenceLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "The module presence file could not be used, so every module counts as present | reason={0}")]
    public void PresenceLoadFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoadFailed, reason);
    }

    public static class PresenceLoadFailureReasons
    {
        // Valid JSON, but no "present" list: nothing says which modules were chosen.
        public const string PresentListMissing = "present_list_missing";
    }
}
