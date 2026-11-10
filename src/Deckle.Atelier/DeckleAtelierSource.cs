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
}
