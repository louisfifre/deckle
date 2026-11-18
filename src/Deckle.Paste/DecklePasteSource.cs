using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Paste;

// Paste to folder's provider. What it writes describes the machine: what kind
// of paste it was, counts, sizes, durations, reasons and error types. What
// the person pasted is theirs, and so is the folder they chose: no event
// carries a text, a byte of an image, a file name or a path.
[EventSource(Name = "Deckle-Paste")]
public sealed class DecklePasteSource : DeckleEventSource
{
    public static readonly DecklePasteSource Log = new();

    private DecklePasteSource() { }

    // ── EventIds ────────────────────────────────────────────────────────
    // IDs are public in the ETW manifest: never reuse one after deleting an
    // event, a consumer may still need it to decode a dump. Two [Event]
    // methods sharing an id silence the whole provider
    // (tests/Deckle.Atelier.Tests/AtelierEventIdBugNotes.md).
    public const int EvtPasteSaved          = 1;
    public const int EvtPasteSavedDetail    = 2;
    public const int EvtPasteSaveFailed     = 3;
    public const int EvtFolderChosen        = 4;
    public const int EvtFolderLoadFailed    = 5;
    public const int EvtFolderSaveFailed    = 6;

    // A paste written to the folder: the outcome the person acted for, so the
    // always-on log keeps it.
    [Event(EvtPasteSaved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Tags = ObservationTags.Milestone,
           Message = "A paste was saved to the chosen folder")]
    public void PasteSaved(string kind)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSaved, kind);
    }

    // bytes counts what was written for a text or an image, and the copied
    // files' sizes for files (a copied folder counts as 0).
    [Event(EvtPasteSavedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "kind={0} | items={1} | bytes={2} | elapsed_ms={3}")]
    public void PasteSavedDetail(string kind, int items, long bytes, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSavedDetail, kind, items, bytes, elapsed_ms);
    }

    // The paste stopped: the window shows Failed with the reason, and the
    // person can try again. saved counts the files copied before it stopped,
    // which stay in the folder.
    [Event(EvtPasteSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "A paste could not be saved; the window shows it failed\nkind={0} | reason={1} | saved={2} | ex_type={3}")]
    public void PasteSaveFailed(string kind, string reason, int saved, string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtPasteSaveFailed, kind, reason, saved, ex_type);
    }

    [Event(EvtFolderChosen,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder was chosen for pastes")]
    public void FolderChosen()
    {
        if (IsEnabled()) WriteEvent(EvtFolderChosen);
    }

    // The file exists and cannot be read: the window opens with no folder,
    // and the unreadable file is kept aside so nothing is lost.
    [Event(EvtFolderLoadFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The paste folder could not be read; the window starts with none\nex_type={0}")]
    public void FolderLoadFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtFolderLoadFailed, ex_type);
    }

    [Event(EvtFolderSaveFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The paste folder could not be saved; the choice holds until Deckle quits\nex_type={0}")]
    public void FolderSaveFailed(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtFolderSaveFailed, ex_type);
    }
}
