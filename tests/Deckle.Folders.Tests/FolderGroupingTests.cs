using Xunit;

namespace Deckle.Folders.Tests;

// What the pane and Home show, in order: the ungrouped folders, then each group.
[Trait("Category", "unit")]
public sealed class FolderGroupingTests
{
    private static AddedFolder Folder(string name, string? groupId = null) =>
        new($@"D:\{name}", DateTimeOffset.UnixEpoch, GroupId: groupId);

    private static readonly FolderGroup Work = new("w", "Work");
    private static readonly FolderGroup Family = new("f", "Family");

    [Fact]
    public void UngroupedFoldersComeFirstInTheirOrderThenEachGroupWithItsFolders()
    {
        AddedFolder[] folders = [Folder("A", "f"), Folder("B"), Folder("C", "w"), Folder("D"), Folder("E", "f")];

        FolderGrouping grouping = FolderGrouping.Of(folders, [Work, Family]);

        Assert.Equal(["B", "D"], grouping.Ungrouped.Select(f => f.Name));
        Assert.Equal(["Work", "Family"], grouping.Groups.Select(g => g.Group.Name));
        Assert.Equal(["C"], grouping.Groups[0].Folders.Select(f => f.Name));
        Assert.Equal(["A", "E"], grouping.Groups[1].Folders.Select(f => f.Name));
    }

    [Fact]
    public void AnEmptyGroupIsListedWithNoFolders()
    {
        FolderGrouping grouping = FolderGrouping.Of([Folder("A")], [Work]);

        GroupedFolders only = Assert.Single(grouping.Groups);
        Assert.Equal(Work, only.Group);
        Assert.Empty(only.Folders);
    }

    [Fact]
    public void AFolderWhoseGroupIsUnknownIsUngrouped()
    {
        FolderGrouping grouping = FolderGrouping.Of([Folder("A", "gone"), Folder("B", "w")], [Work]);

        Assert.Equal(["A"], grouping.Ungrouped.Select(f => f.Name));
        Assert.Equal(["B"], grouping.Groups.Single().Folders.Select(f => f.Name));
    }

    [Fact]
    public void InPaneOrderListsTheUngroupedThenEachGroupsFolders()
    {
        AddedFolder[] folders = [Folder("A", "f"), Folder("B"), Folder("C", "w"), Folder("D", "f")];

        FolderGrouping grouping = FolderGrouping.Of(folders, [Work, Family]);

        Assert.Equal(["B", "C", "A", "D"], grouping.InPaneOrder.Select(f => f.Name));
    }

    [Fact]
    public void NothingAddedGivesNothingToShow()
    {
        FolderGrouping grouping = FolderGrouping.Of([], []);

        Assert.Empty(grouping.Ungrouped);
        Assert.Empty(grouping.Groups);
        Assert.Empty(grouping.InPaneOrder);
    }
}
