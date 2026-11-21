using Xunit;
using Deckle.Core;

namespace Deckle.Folders.Tests;

// The place built on the path rules (FolderPaths, in Core): what the address
// bar shows and where Up stops.
[Trait("Category", "unit")]
public sealed class PathRulesTests
{
    private static readonly AddedFolder Photos = new(@"D:\Photos", DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheSegmentsStartAtTheAddedFolderAndEndAtThePlace()
    {
        var place = new FolderPlace(Photos, @"D:\Photos\2026\2026-06-14 Mariage");

        Assert.Equal(
            [("Photos", @"D:\Photos"), ("2026", @"D:\Photos\2026"), ("2026-06-14 Mariage", @"D:\Photos\2026\2026-06-14 Mariage")],
            place.Segments.Select(s => (s.Name, s.Path)));
    }

    [Fact]
    public void UpStopsAtTheAddedFolder()
    {
        var place = new FolderPlace(Photos, @"D:\Photos\2026");

        Assert.Equal(FolderPlace.AtRoot(Photos), place.Parent);
        Assert.Null(place.Parent!.Parent);
    }

    [Fact]
    public void APlaceOutsideItsAddedFolderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new FolderPlace(Photos, @"D:\Archives"));
    }

    [Fact]
    public void TwoSpellingsOfOnePlaceAreEqual()
    {
        Assert.Equal(new FolderPlace(Photos, @"D:\Photos\2026"), new FolderPlace(Photos, @"d:\PHOTOS\2026\"));
    }
}
