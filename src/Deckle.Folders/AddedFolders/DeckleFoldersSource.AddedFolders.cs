using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The added folders' removal and their unreachable roots take the block 70 to
// 79, given to this work so the lanes built beside it each keep their own: two
// [Event] methods sharing an id silence the whole provider
// (tests/Deckle.Folders.Tests/FoldersEventIdBugNotes.md).
//
// Like the rest of the provider, an event here carries counts and reasons,
// never a folder's name or path.
public sealed partial class DeckleFoldersSource
{
    public const int EvtFolderRemoved          = 70;
    public const int EvtFolderRemovedDetail    = 71;
    public const int EvtAddedFolderUnreachable = 72;

    // The person removed a folder from the app. Only the entry goes: nothing
    // on the disk is touched.
    [Event(EvtFolderRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder was removed from Deckle Folders; nothing on the disk changed")]
    public void FolderRemoved()
    {
        if (IsEnabled()) WriteEvent(EvtFolderRemoved);
    }

    [Event(EvtFolderRemovedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "folders={0}")]
    public void FolderRemovedDetail(int folders)
    {
        if (IsEnabled()) WriteEvent(EvtFolderRemovedDetail, folders);
    }

    // The window opened an added folder whose path is gone or refuses to be
    // read: it shows a message in place of the tool and goes on.
    [Event(EvtAddedFolderUnreachable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An added folder could not be reached; the window shows it as missing\nreason={0}")]
    public void AddedFolderUnreachable(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtAddedFolderUnreachable, reason);
    }
}
