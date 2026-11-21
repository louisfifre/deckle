using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The map's large graph. EventIds 48-49 belong to it, the end of the map's
// block (40-49, DeckleFoldersSource.cs), so the lanes built beside it keep
// their own numbers. Two events sharing a number put the whole source in
// error and it then writes nothing at all (FoldersEventIdBugNotes.md).
public sealed partial class DeckleFoldersSource
{
    public const int EvtMapGraphTrimmed = 48;

    // The large graph left items out because its columns could not hold
    // them readably: it drew the chosen item's neighbourhood, or with none
    // chosen the first items by name, and said "+N" for the rest.
    [Event(EvtMapGraphTrimmed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "items_drawn={0} | items_hidden={1} | neighbourhood={2}")]
    public void MapGraphTrimmed(int items_drawn, int items_hidden, bool neighbourhood)
    {
        if (IsEnabled()) WriteEvent(EvtMapGraphTrimmed, items_drawn, items_hidden, neighbourhood);
    }
}
