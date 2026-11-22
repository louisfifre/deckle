using Xunit;

namespace Deckle.Core.Tests;

// The path rules every part of Deckle that reads a folder agrees on.
[Trait("Category", "unit")]
public sealed class FolderPathsTests
{
    [Fact]
    public void AFolderWhoseNameStartsLikeTheRootIsNotInsideIt()
    {
        Assert.False(FolderPaths.IsInside(@"C:\Photos", @"C:\Photos2\a"));
        Assert.True(FolderPaths.IsInside(@"C:\Photos", @"c:\photos\2026\"));
        Assert.True(FolderPaths.IsInside(@"C:\", @"C:\Photos"));
    }

    [Fact]
    public void ADriveRootIsNamedByItsLetter()
    {
        Assert.Equal("C:", FolderPaths.DisplayName(@"C:\"));
        Assert.Equal("Photos", FolderPaths.DisplayName(@"C:\Photos\"));
    }
}
