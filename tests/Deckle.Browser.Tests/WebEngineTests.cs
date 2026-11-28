using Xunit;

namespace Deckle.Browser.Tests;

// Whether the web engine is older than the minimum.
[Trait("Category", "unit")]
public sealed class WebEngineTests
{
    [Theory]
    [InlineData("139.0.3405.1", true)]
    [InlineData("100.0.0.0", true)]
    [InlineData("140.0.3485.54", false)]
    [InlineData("147.0.1.2", false)]
    public void AMajorBelowTheMinimumIsOutOfDate(string version, bool expected)
    {
        Assert.Equal(expected, WebEngine.IsOutOfDate(version));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void AnUnreadableVersionIsNotCalledOutOfDate(string? version)
    {
        Assert.False(WebEngine.IsOutOfDate(version));
    }

    [Fact]
    public void TheMinimumMajorIs140()
    {
        Assert.Equal(140, WebEngine.MinimumMajor);
    }
}
