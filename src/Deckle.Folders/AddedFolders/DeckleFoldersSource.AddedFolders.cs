using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Folders;

// The added folders' removal, their unreachable roots and their groups take the
// block 70 to 79, given to this work so the lanes built beside it each keep
// their own: two [Event] methods sharing an id silence the whole provider
// (tests/Deckle.Folders.Tests/FoldersEventIdBugNotes.md).
//
// Like the rest of the provider, an event here carries counts and reasons,
// never a folder's name or path.
public sealed partial class DeckleFoldersSource
{
    public const int EvtFolderRemoved          = 70;
    public const int EvtFolderRemovedDetail    = 71;
    public const int EvtAddedFolderUnreachable = 72;
    public const int EvtFolderGroupAdded          = 73;
    public const int EvtFolderGroupAddedDetail    = 74;
    public const int EvtFolderGroupRenamed        = 75;
    public const int EvtFolderGroupRemoved        = 76;
    public const int EvtFolderGroupRemovedDetail  = 77;
    public const int EvtFolderMovedToGroup        = 78;
    public const int EvtFolderMovedToGroupDetail  = 79;

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

    // The person made a group of added folders. Its name is theirs and stays
    // out of the log; the detail counts.
    [Event(EvtFolderGroupAdded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder group was added in Deckle Folders")]
    public void FolderGroupAdded()
    {
        if (IsEnabled()) WriteEvent(EvtFolderGroupAdded);
    }

    [Event(EvtFolderGroupAddedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "groups={0}")]
    public void FolderGroupAddedDetail(int groups)
    {
        if (IsEnabled()) WriteEvent(EvtFolderGroupAddedDetail, groups);
    }

    [Event(EvtFolderGroupRenamed,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder group was renamed in Deckle Folders")]
    public void FolderGroupRenamed()
    {
        if (IsEnabled()) WriteEvent(EvtFolderGroupRenamed);
    }

    // Only the group goes: its folders stay added, in no group.
    [Event(EvtFolderGroupRemoved,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A folder group was removed from Deckle Folders; its folders stay added, in no group")]
    public void FolderGroupRemoved()
    {
        if (IsEnabled()) WriteEvent(EvtFolderGroupRemoved);
    }

    [Event(EvtFolderGroupRemovedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "groups={0} | folders_released={1}")]
    public void FolderGroupRemovedDetail(int groups, int folders_released)
    {
        if (IsEnabled()) WriteEvent(EvtFolderGroupRemovedDetail, groups, folders_released);
    }

    // An added folder joined a group or left one for no group.
    [Event(EvtFolderMovedToGroup,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "An added folder was moved to another place in the pane")]
    public void FolderMovedToGroup()
    {
        if (IsEnabled()) WriteEvent(EvtFolderMovedToGroup);
    }

    [Event(EvtFolderMovedToGroupDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "in_group={0} | folders_in_place={1}")]
    public void FolderMovedToGroupDetail(bool in_group, int folders_in_place)
    {
        if (IsEnabled()) WriteEvent(EvtFolderMovedToGroupDetail, in_group, folders_in_place);
    }
}
