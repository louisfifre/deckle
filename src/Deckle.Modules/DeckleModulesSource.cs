using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Modules;

// Module catalogue and presence provider. Covers catalogue registration
// (ModuleRegistry) and the presence choice's load/save lifecycle
// (ModulePresence / PresenceFile).
//
// Verbose/Info separation per Deckle.Diagnostics/CLAUDE.md: an Info / Warning
// is a short Capital sentence with no ids, paths, or k=v; the technical detail
// (module ids, csv lists, paths, exception type+message) lives in a Verbose
// mirror beside it.
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
    public const int EvtPresenceLoadFailedDetail  = 7;

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
           Message = "module presence loaded | mode={0} | present={1}")]
    public void PresenceLoadedDetail(string mode, string present)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoadedDetail, mode, present);
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
           Message = "module selection saved | present={0} | path={1}")]
    public void PresenceSavedDetail(string present, string path)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceSavedDetail, present, path);
    }

    [Event(EvtPresenceLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Could not read the module presence file")]
    public void PresenceLoadFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoadFailed);
    }

    [Event(EvtPresenceLoadFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "presence load failed | error={0} | message={1} | path={2}")]
    public void PresenceLoadFailedDetail(string ex_type, string message, string path)
    {
        if (IsEnabled()) WriteEvent(EvtPresenceLoadFailedDetail, ex_type, message, path);
    }
}
