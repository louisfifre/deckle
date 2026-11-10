using Xunit;

namespace Deckle.Atelier.Tests;

// The path rules every part of the Atelier agrees on, and the place built on
// them: what the address bar shows and where Up stops.
[Trait("Category", "unit")]
public sealed class PathRulesTests
{
    private static readonly AddedFolder Photos = new(@"D:\Photos", DateTimeOffset.UnixEpoch);

    [Fact]
    public void AFolderWhoseNameStartsLikeTheRootIsNotInsideIt()
    {
        Assert.False(FolderPaths.IsInside(@"D:\Photos", @"D:\Photos2\a"));
        Assert.True(FolderPaths.IsInside(@"D:\Photos", @"d:\photos\2026\"));
        Assert.True(FolderPaths.IsInside(@"D:\", @"D:\Photos"));
    }

    [Fact]
    public void ADriveRootIsNamedByItsLetter()
    {
        Assert.Equal("D:", FolderPaths.DisplayName(@"D:\"));
        Assert.Equal("Photos", FolderPaths.DisplayName(@"D:\Photos\"));
    }

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
