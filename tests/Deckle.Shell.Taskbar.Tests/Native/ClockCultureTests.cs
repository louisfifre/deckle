using System.Globalization;
using Xunit;

namespace Deckle.Shell.Taskbar.Tests;

// The compact clock follows the system: the language of its names, the 12 or
// 24 hour clock of the regional format, and the theme's own upper-line color.
[Trait("Category", "unit")]
public sealed class ClockCultureTests
{
    private static ClockCustomization Clock(string names, string regional)
    {
        var settings = new TaskbarSettings { CompactClock = true };
        return TaskbarCustomization.From(settings, verticalBar: true, new CultureInfo(names), new CultureInfo(regional)).Clock!;
    }

    [Fact]
    public void TheClockNamesFollowTheDisplayLanguage()
    {
        Assert.Equal("fr-FR", Clock("fr-FR", "fr-FR").Locale);
        Assert.Equal("en-US", Clock("en-US", "en-US").Locale);
    }

    [Fact]
    public void TheHourFollowsTheRegionalTwelveOrTwentyFourHourFormat()
    {
        Assert.Equal("HH", Clock("fr-FR", "fr-FR").LowerLines[0].Format);
        Assert.Equal("hh", Clock("en-US", "en-US").LowerLines[0].Format);
    }

    [Fact]
    public void TheUpperLineKeepsExplorersOwnColorSoItFollowsTheTheme()
    {
        Assert.Null(Clock("en-US", "en-US").UpperStyle.Color);
    }
}
