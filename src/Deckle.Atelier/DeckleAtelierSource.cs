using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Atelier;

// The Atelier's provider. What it writes describes the machine: counts,
// durations, reasons and error types. An added folder's path is the person's
// own disk, so no event carries a path or a name.
[EventSource(Name = "Deckle-Atelier")]
public sealed class DeckleAtelierSource : DeckleEventSource
{
    public static readonly DeckleAtelierSource Log = new();

    private DeckleAtelierSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest: never reuse one after deleting an
    // event, a consumer may still need it to decode a dump.
    public const int EvtAddedFoldersLoadFailed = 1;
    public const int EvtAddedFoldersSaveFailed = 2;
    public const int EvtFolderAdded            = 3;
    public const int EvtFolderAddedDetail      = 4;
    public const int EvtFolderListed           = 5;
    public const int EvtFolderListingFailed    = 6;
    public const int EvtToolShown              = 7;
    public const int EvtToolFailed             = 8;

    // The Sizes engine's block starts at 30, so the tools built side by side
    // each add theirs without taking the next free number from one another.
    public const int EvtSizeScanStarted        = 30;
    public const int EvtSizeScanEnded          = 31;
    public const int EvtSizeScanPartial        = 32;
    public const int EvtSizeScanFailed         = 33;

    // The map's events take 40 to 49, so the engines built beside it each
    // add theirs without touching these lines.
    public const int EvtMapInventoryBuilt      = 40;
    public const int EvtMapInventoryFailed     = 41;
    public const int EvtMapInventoryTruncated  = 42;
    public const int EvtMapInventoryCancelled  = 43;
    public const int EvtMapConfigUnreadable    = 44;

    // The file exists and cannot be read: the window opens with no folder,
    // and the unreadable file is kept aside so nothing is lost.
    [Event(EvtAddedFoldersLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The added folders could not be read; the Atelier starts with none\nex_type={0}")]
    public void AddedFoldersLoadFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtAddedFoldersLoadFailed, ex_type);
    }

    [Event(EvtAddedFoldersSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The added folders could not be saved; the change holds until the window closes\nex_type={0}")]
    public void AddedFoldersSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtAddedFoldersSaveFailed, ex_type);
    }

    [Event(EvtFolderAdded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder was added to the Atelier")]
    public void FolderAdded()
    {
        if (IsEnabled()) WriteEvent(EvtFolderAdded);
    }

    [Event(EvtFolderAddedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folders={0}")]
    public void FolderAddedDetail(int folders)
    {
        if (IsEnabled()) WriteEvent(EvtFolderAddedDetail, folders);
    }

    [Event(EvtFolderListed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "entries={0} | elapsed_ms={1}")]
    public void FolderListed(long entries, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtFolderListed, entries, elapsed_ms);
    }

    // The folder vanished or is not readable: the window shows it as such
    // and goes on.
    [Event(EvtFolderListingFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A folder could not be listed; the tool shows it as unreadable\nreason={0}")]
    public void FolderListingFailed(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtFolderListingFailed, reason);
    }

    [Event(EvtToolShown,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tool={0} | elapsed_ms={1}")]
    public void ToolShown(string tool, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtToolShown, tool, elapsed_ms);
    }

    // A tool that throws while it shows a folder: the window keeps the other
    // tools and the folder.
    [Event(EvtToolFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A tool failed to show the folder; the window goes on without it\ntool={0} | ex_type={1}")]
    public void ToolFailed(string tool, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtToolFailed, tool, ex_type);
    }

    // A size analysis of a place: started, then ended with its outcome
    // ("Completed", "Cancelled", "Failed") and its counts. scan_id pairs the two
    // in the file; the place's path stays out.
    [Event(EvtSizeScanStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A size analysis started")]
    public void SizeScanStarted(long scan_id, long workers)
    {
        if (IsEnabled()) WriteEvent(EvtSizeScanStarted, scan_id, workers);
    }

    [Event(EvtSizeScanEnded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "A size analysis ended")]
    public void SizeScanEnded(long scan_id, string outcome, long folders, long files,
        long unreadable_folders, long links_skipped, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtSizeScanEnded, scan_id, outcome, folders, files,
            unreadable_folders, links_skipped, elapsed_ms);
    }

    // Folders beneath the place refused or failed to list: one line per
    // analysis, not per folder, and the sizes shown leave them out.
    [Event(EvtSizeScanPartial,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Some folders could not be read; the sizes leave them out\nscan_id={0} | unreadable_folders={1}")]
    public void SizeScanPartial(long scan_id, long unreadable_folders)
    {
        if (IsEnabled()) WriteEvent(EvtSizeScanPartial, scan_id, unreadable_folders);
    }

    [Event(EvtSizeScanFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A size analysis stopped on an unexpected error\nscan_id={0} | ex_type={1}")]
    public void SizeScanFailed(long scan_id, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtSizeScanFailed, scan_id, ex_type);
    }

    // ── Map ─────────────────────────────────────────────────────────────

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
