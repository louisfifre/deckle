using Deckle.Catalog;
using Xunit;

namespace Deckle.Catalog.Tests;

// The body of the "Reset this page?" dialog is the same sentence on every page,
// with the page's own detail after it when it has one.
[Trait("Category", "unit")]
public sealed class ResetAllWordingTests
{
    [Fact]
    public void WithoutDetailTheBodyIsTheIntroAlone()
    {
        Assert.Equal("Intro.", ResetAllWording.Body("Intro.", null));
        Assert.Equal("Intro.", ResetAllWording.Body("Intro.", ""));
        Assert.Equal("Intro.", ResetAllWording.Body("Intro.", "   "));
    }

    [Fact]
    public void DetailFollowsTheIntroAfterABlankLine()
    {
        Assert.Equal("Intro.\n\nDetail.", ResetAllWording.Body("Intro.", " Detail. "));
    }
}
