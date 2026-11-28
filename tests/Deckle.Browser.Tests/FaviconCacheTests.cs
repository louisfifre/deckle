using Xunit;

namespace Deckle.Browser.Tests;

[Trait("Category", "unit")]
public sealed class FaviconCacheTests
{
    [Theory]
    [InlineData("example.com", "example.com.png")]
    [InlineData("Example.COM", "example.com.png")]
    [InlineData("localhost:5000", "localhost_5000.png")]
    [InlineData("", "_.png")]
    public void TheFileIsNamedFromTheLowercaseHost(string host, string expected)
    {
        Assert.Equal(expected, FaviconCache.FileName(host));
    }
}
