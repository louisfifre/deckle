using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.FolderMap;

// The map's provider. What it writes describes the machine: counts, durations,
// reasons and error types. A mapped folder's path is the person's own disk, so
// no event carries a path or a name. The five events were Deckle-Atelier's
// before the map became support code shared with Deckle Projects: a log filter
// saved on Deckle-Atelier no longer sees them.
[EventSource(Name = "Deckle-FolderMap")]
public sealed class DeckleFolderMapSource : DeckleEventSource
{
    public static readonly DeckleFolderMapSource Log = new();

    private DeckleFolderMapSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest: never reuse one after deleting an
    // event, a consumer may still need it to decode a dump.
    public const int EvtMapInventoryBuilt      = 1;
    public const int EvtMapInventoryFailed     = 2;
    public const int EvtMapInventoryTruncated  = 3;
    public const int EvtMapInventoryCancelled  = 4;
    public const int EvtMapConfigUnreadable    = 5;

    [Event(EvtMapInventoryBuilt,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "items={0} | links={1} | skipped={2} | elapsed_ms={3}")]
    public void MapInventoryBuilt(int items, int links, int skipped, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtMapInventoryBuilt, items, links, skipped, elapsed_ms);
    }

    // The folder vanished or is not readable: the map shows it as such.
    [Event(EvtMapInventoryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A folder could not be mapped; the map shows it as unreadable\nreason={0}")]
    public void MapInventoryFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtMapInventoryFailed, reason);
    }

    // A folder larger than the limit: the map shows what it saw and says it
    // is partial.
    [Event(EvtMapInventoryTruncated,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The map reached its entry limit; the inventory is partial\nmax_entries={0}")]
    public void MapInventoryTruncated(int max_entries)
    {
        if (IsEnabled()) WriteEvent(EvtMapInventoryTruncated, max_entries);
    }

    // The person left the folder or the tool before the map was done.
    [Event(EvtMapInventoryCancelled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "elapsed_ms={0}")]
    public void MapInventoryCancelled(long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtMapInventoryCancelled, elapsed_ms);
    }

    [Event(EvtMapConfigUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "An MCP configuration file could not be read; its servers are left out of the map\nex_type={0}")]
    public void MapConfigUnreadable(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtMapConfigUnreadable, ex_type);
    }
}
