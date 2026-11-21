using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// Deckle Folders' provider. What it writes describes the machine: counts,
// durations, reasons and error types. An added folder's path is the person's
// own disk, so no event carries a path or a name.
[EventSource(Name = "Deckle-Folders")]
public sealed partial class DeckleFoldersSource : DeckleEventSource
{
    public static readonly DeckleFoldersSource Log = new();

    private DeckleFoldersSource() { }

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

    // 40 to 44 were the map's inventory events, now Deckle-FolderMap's
    // (Deckle.FolderMap). The numbers stay retired here, never reused; 45 to
    // 49 stay with the map's graph (MapGraph/DeckleFoldersSource.MapGraph.cs).

    // The added folders' icons take 50 to 59.
    public const int EvtFolderIconChanged       = 50;
    public const int EvtFolderIconChangedDetail = 51;

    // The file exists and cannot be read: the window opens with no folder,
    // and the unreadable file is kept aside so nothing is lost.
    [Event(EvtAddedFoldersLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The added folders could not be read; Deckle Folders starts with none\nex_type={0}")]
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
           Message = "A folder was added to Deckle Folders")]
    public void FolderAdded()
    {
        if (IsEnabled()) WriteEvent(EvtFolderAdded);
    }

    [Event(EvtFolderAddedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folders={0} | icon={1}",
           Version = 1)]
    public void FolderAddedDetail(int folders, string icon)
    {
        if (IsEnabled()) WriteEvent(EvtFolderAddedDetail, folders, icon);
    }

    // The person gave an added folder another icon with the picker. The
    // detail says the choice: names of the fixed set, nothing of the folder.
    [Event(EvtFolderIconChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An added folder's icon was changed")]
    public void FolderIconChanged()
    {
        if (IsEnabled()) WriteEvent(EvtFolderIconChanged);
    }

    [Event(EvtFolderIconChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "icon={0} | tint={1} | background={2}")]
    public void FolderIconChangedDetail(string icon, string tint, bool background)
    {
        if (IsEnabled()) WriteEvent(EvtFolderIconChangedDetail, icon, tint, background);
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
}
