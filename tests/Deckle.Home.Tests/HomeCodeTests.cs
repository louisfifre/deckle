using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

[Trait("Category", "unit")]
public class HomeCodeTests
{
    [Theory]
    [InlineData("ZZ-PS01", "ZZ", "PS", 1)]
    [InlineData("YY-SD12", "YY", "SD", 12)]
    public void ElementCodeSeparatesRoomCategoryAndSequence(
        string value, string room, string category, int sequence)
    {
        HomeElementCode parsed = HomeElementCode.Parse(value);

        Assert.Equal(room, parsed.Room);
        Assert.Equal(category, parsed.Category);
        Assert.Equal(sequence, parsed.Sequence);
    }

    [Theory]
    [InlineData("Z-P01")]
    [InlineData("ZZ-PS00")]
    [InlineData("ZZ-Q01")]
    [InlineData("ZZP01")]
    public void ElementCodeRejectsValuesOutsideThePublicGrammar(string value)
    {
        Assert.Throws<ArgumentException>(() => HomeElementCode.Parse(value));
    }

    [Fact]
    public void CategoryValidatesAgainstTheRetainedManifestAndLowercasesTheOptionKey()
    {
        Assert.Equal("PS", HomeCategories.Validate("ps"));
        Assert.Equal("pn", HomeCategories.OptionKey("PN"));
        Assert.Throws<ArgumentException>(() => HomeCategories.Validate("RJ"));
        Assert.Throws<ArgumentException>(() => HomeCategories.Validate("Q"));
    }
}
