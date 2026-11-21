using Xunit;

namespace Deckle.Core.Tests;

// The path rules every part of Deckle that reads a folder agrees on.
[Trait("Category", "unit")]
public sealed class FolderPathsTests
{
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
}
