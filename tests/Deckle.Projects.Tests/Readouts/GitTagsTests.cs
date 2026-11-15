using Xunit;

namespace Deckle.Projects.Tests;

[Trait("Category", "unit")]
public sealed class GitTagsTests
{
    [Fact]
    public void TheHighestIsChosenByNumberNotByText()
    {
        Assert.Equal("v0.131.0", GitTags.Highest(["v0.32.0", "v0.131.0", "v0.9.0"]));
        Assert.Equal("v1.10.0", GitTags.Highest(["v1.9.0", "v1.10.0", "v1.2.0"]));
    }

    [Fact]
    public void ATagWithoutTheVIsReadAndShownWithOne()
    {
        Assert.Equal("v0.32.0", GitTags.Highest(["0.32.0"]));
        Assert.Equal("v0.32.0", GitTags.Highest(["0.32.0", "v0.31.0"]));
    }

    [Fact]
    public void TheVIsAddedOnceWhateverTheMix()
    {
        Assert.Equal("v2.0.0", GitTags.Highest(["V2.0.0"]));
        Assert.Equal("v2.0.0", GitTags.Highest(["2.0.0", "v2.0.0"]));
    }

    [Theory]
    [InlineData("v1.0.0-rc.1")]
    [InlineData("latest")]
    [InlineData("release-2")]
    [InlineData("v1.0")]
    [InlineData("2026-11-15")]
    [InlineData("")]
    public void ATagThatIsNotAReleaseNumberIsIgnored(string tag)
    {
        Assert.Null(GitTags.Highest([tag]));
        Assert.Equal("v0.1.0", GitTags.Highest([tag, "v0.1.0"]));
    }

    [Fact]
    public void NoTagGivesNull()
    {
        Assert.Null(GitTags.Highest([]));
    }

    [Fact]
    public void ANumberTooBigToCompareIsIgnoredNotACrash()
    {
        Assert.Equal("v1.0.0", GitTags.Highest(["v99999999999999999999.0.0", "v1.0.0"]));
    }

    [Theory]
    [InlineData("v0.32.0", "0.32.0")]
    [InlineData("0.32.0", "0.32.0")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("vendor", "vendor")]
    public void TheVIsDroppedWhenItIsAVersionsPrefix(string version, string expected)
    {
        Assert.Equal(expected, GitTags.WithoutV(version));
    }
}
