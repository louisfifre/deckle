namespace Deckle.Folders;

// What the pane and Home show of the added folders, in order: the folders in
// no group first, then each group with its folders. Both surfaces read this
// one type so they show one order; neither sorts on its own.
//
// Pure: it reads the store's two lists and touches nothing. A folder whose
// GroupId names no group (a file edited by hand, a group removed elsewhere)
// is shown as in no group rather than lost.
public sealed record FolderGrouping(IReadOnlyList<AddedFolder> Ungrouped, IReadOnlyList<GroupedFolders> Groups)
{
    // The folders keep the order of "folders" inside each place; the groups
    // keep the order of "groups", the empty ones included so the person can
    // still see, rename or remove a group they emptied.
    public static FolderGrouping Of(IReadOnlyList<AddedFolder> folders, IReadOnlyList<FolderGroup> groups)
    {
        var known = new HashSet<string>(groups.Select(g => g.Id), StringComparer.Ordinal);
        AddedFolder[] ungrouped = [.. folders.Where(f => f.GroupId is null || !known.Contains(f.GroupId))];
        GroupedFolders[] grouped =
        [
            .. groups.Select(g => new GroupedFolders(g, [.. folders.Where(f => f.GroupId == g.Id)])),
        ];
        return new FolderGrouping(ungrouped, grouped);
    }

    // Every folder in the order the pane shows them: the ungrouped ones, then each group's.
    public IEnumerable<AddedFolder> InPaneOrder => Ungrouped.Concat(Groups.SelectMany(g => g.Folders));
}

// A group and the folders in it, in the order the person sees them.
public sealed record GroupedFolders(FolderGroup Group, IReadOnlyList<AddedFolder> Folders);
