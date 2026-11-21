using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The file tool's events about the items it sends to the Recycle Bin. They
// take EventIds 62 to 65, inside the file tool's block of 60 to 69, whose 60
// and 61 are its launches (Launching/DeckleFoldersSource.Launching.cs). Two
// events sharing a number put the whole source in error and it then writes
// nothing at all (tests/Deckle.Folders.Tests/FoldersEventIdBugNotes.md).
//
// Like the rest of the provider, an event here carries counts and durations,
// never a name or a path.
public sealed partial class DeckleFoldersSource
{
    public const int EvtItemsRecycled = 62;

    // The file tool sent items to the Recycle Bin: one by its row's action,
    // the whole selection by the Delete key. Each item the bin refused already
    // has its own warning (EvtRecycleRefused); this line says how the whole
    // gesture ended.
    [Event(EvtItemsRecycled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "items={0} | recycled={1} | stayed={2} | deleted_for_good={3} | elapsed_ms={4}")]
    public void ItemsRecycled(int items, int recycled, int stayed, int deleted_for_good, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtItemsRecycled, items, recycled, stayed, deleted_for_good, elapsed_ms);
    }
}
