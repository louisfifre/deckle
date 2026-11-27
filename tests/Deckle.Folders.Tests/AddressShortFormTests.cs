using Xunit;

namespace Deckle.Folders.Tests;

// The field at rest: drive, an ellipsis for what is hidden, the current name.
[Trait("Category", "unit")]
public sealed class AddressShortFormTests
{
    private static AddedFolder Added(string path) => new(path, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AtAnAddedFolderTheDriveAndTheFolderShowWithNothingHidden()
    {
        AddressShortForm form = AddressShortForm.Of(FolderPlace.AtRoot(Added(@"C:\Code\Photos")));

        Assert.Equal(new AddressShortForm("C:", false, "Photos"), form);
    }

    [Fact]
    public void ThreeLevelsDownHidesTheSegmentsAndNamesTheCurrentFolder()
    {
        var place = new FolderPlace(Added(@"C:\Code\Photos"), @"C:\Code\Photos\2026\2026-06-14 Mariage");

        Assert.Equal(new AddressShortForm("C:", true, "2026-06-14 Mariage"), AddressShortForm.Of(place));
    }

    [Fact]
    public void AnAddedDriveRootHasNoDriveLabel()
    {
        AddressShortForm form = AddressShortForm.Of(FolderPlace.AtRoot(Added(@"D:\")));

        Assert.Null(form.DriveLabel);
        Assert.False(form.HidesSegments);
        Assert.Equal("D:", form.CurrentName);
    }

    [Fact]
    public void AUncAddedFolderTakesItsShareAsTheDriveLabel()
    {
        var place = new FolderPlace(Added(@"\\server\share\Photos"), @"\\server\share\Photos\2026");

        Assert.Equal(new AddressShortForm(@"\\server\share", true, "2026"), AddressShortForm.Of(place));
    }

    [Fact]
    public void AnAddedUncShareRootHasNoDriveLabel()
    {
        Assert.Null(AddressShortForm.Of(FolderPlace.AtRoot(Added(@"\\server\share"))).DriveLabel);
    }
}
