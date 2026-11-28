using Xunit;

namespace Deckle.Browser.Tests;

// What the address field shows for a page when it is not being edited.
[Trait("Category", "unit")]
public sealed class SiteNameTests
{
    [Theory]
    [InlineData("https://example.com/a/b?c=d", "example.com")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("https://mail.example.com/", "example.com")]
    [InlineData("https://mail.example.co.uk/", "example.co.uk")]
    [InlineData("https://localhost:5000/", "localhost")]
    [InlineData("https://www.example.com/", "example.com")]
    public void AWebPageShowsItsRegistrableDomainAlone(string uri, string expected)
    {
        Assert.Equal(expected, SiteName.Display(new Uri(uri)));
    }

    [Fact]
    public void AHostWithNonAsciiCharactersShowsItsPunycodeForm()
    {
        string shown = SiteName.Display(new Uri("https://www.bücher.example/"));
        Assert.Equal("xn--bcher-kva.example", shown);
        Assert.All(shown, c => Assert.True(char.IsAscii(c)));
    }

    [Fact]
    public void ALookAlikeInAnotherAlphabetIsNeverShownAsTheHostItImitates()
    {
        // Cyrillic "a" in place of the Latin one.
        string shown = SiteName.Display(new Uri("https://аpple.com/"));
        Assert.StartsWith("xn--", shown);
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("file:///C:/temp/page.html")]
    [InlineData("data:text/html,hi")]
    public void APageThatIsNotOnTheWebShowsNothing(string uri)
    {
        Assert.Equal("", SiteName.Display(new Uri(uri)));
    }

    [Fact]
    public void NoPageShowsNothing()
    {
        Assert.Equal("", SiteName.Display(null));
    }

    [Theory]
    [InlineData("http://example.com/", true)]
    [InlineData("https://example.com/", false)]
    [InlineData("about:blank", false)]
    public void OnlyPlainHttpIsNotSecure(string uri, bool expected)
    {
        Assert.Equal(expected, SiteName.IsNotSecure(new Uri(uri)));
    }
}
