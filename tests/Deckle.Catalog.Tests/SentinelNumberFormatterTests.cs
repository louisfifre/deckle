using Deckle.Catalog;
using Windows.Globalization.NumberFormatting;
using Xunit;

namespace Deckle.Catalog.Tests;

// A number field with a sentinel shows the word in its place and reads the word
// back, so a person reads "Auto" where the engine stores -1; every other value
// reads and parses as the plain field would.
[Trait("Category", "unit")]
public class SentinelNumberFormatterTests
{
    private static SentinelNumberFormatter Formatter() =>
        new(new DecimalFormatter { IntegerDigits = 1, FractionDigits = 0, IsGrouped = false }, -1, "Auto");

    [Fact]
    public void TheSentinelReadsAsTheWord()
    {
        Assert.Equal("Auto", Formatter().FormatDouble(-1));
        Assert.Equal("Auto", Formatter().FormatInt(-1));
    }

    [Fact]
    public void AnyOtherValueReadsAsTheNumber()
    {
        Assert.Equal("224", Formatter().FormatDouble(224));
        Assert.Equal("0", Formatter().FormatDouble(0));
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("auto")]
    [InlineData("  AUTO ")]
    public void TheWordParsesBackToTheSentinel(string typed)
    {
        Assert.Equal(-1, Formatter().ParseDouble(typed));
        Assert.Equal(-1L, Formatter().ParseInt(typed));
    }

    [Fact]
    public void ANumberParsesAsTheNumberTheSentinelIncluded()
    {
        Assert.Equal(224, Formatter().ParseDouble("224"));
        Assert.Equal(-1, Formatter().ParseDouble("-1"));
    }

    [Fact]
    public void TextThatIsNeitherParsesAsNothing()
    {
        Assert.Null(Formatter().ParseDouble("automatic"));
    }
}
