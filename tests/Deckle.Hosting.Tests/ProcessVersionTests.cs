using Xunit;

namespace Deckle.Hosting.Tests;

[Trait("Category", "unit")]
public sealed class ProcessVersionTests
{
    [Theory]
    [InlineData("0.141.0+526d0375", "0.141.0")]
    [InlineData("0.141.0", "0.141.0")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("+abc", "+abc")]
    public void TheCommitSuffixIsDropped(string? productVersion, string expected)
    {
        Assert.Equal(expected, ProcessVersion.Trim(productVersion));
    }
}
