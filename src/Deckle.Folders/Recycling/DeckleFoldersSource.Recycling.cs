using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The Recycle Bin's events, shared by every tool that sends an item there:
// the photo tool's application of the marks and the file tool's row action.
// They keep the numbers they had in the photo tool's block, where they were
// born; an ID is never reused nor moved.
//
// Like the rest of the provider, an event here carries reasons, codes and
// extensions, never a name or a path.
public sealed partial class DeckleFoldersSource
{
    public const int EvtRecycleRefused     = 105;
    public const int EvtItemDeletedForGood = 106;

    // The shell said it would not recycle the item, or failed: the item is
    // left in its folder, never deleted for good, and the work goes on.
    [Event(EvtRecycleRefused,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "An item stayed in its folder because the Recycle Bin could not take it\nreason={0} | hresult={1}")]
    public void RecycleRefused(string reason, int hresult)
    {
        if (IsEnabled()) WriteEvent(EvtRecycleRefused, reason, hresult);
    }

    // The guard did not hold: the shell reported a deletion with no item in
    // the Recycle Bin. Error, because the person was promised the bin.
    [Event(EvtItemDeletedForGood,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "An item was deleted for good instead of going to the Recycle Bin\nextension={0}")]
    public void ItemDeletedForGood(string extension)
    {
        if (IsEnabled()) WriteEvent(EvtItemDeletedForGood, extension);
    }
}
