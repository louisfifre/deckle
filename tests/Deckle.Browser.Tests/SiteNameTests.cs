using Xunit;

namespace Deckle.Browser.Tests;

// What the address field shows for a page when it is not being edited.
[Trait("Category", "unit")]
public sealed class SiteNameTests
{
    [Theory]
    [InlineData("https://example.com/a/b?c=d", "example.com")]
    [InlineData("http://example.com", "example.com")]
    [InlineData("https://mail.example.com/", "mail.example.com")]
    [InlineData("https://localhost:5000/", "localhost")]
    public void AWebPageShowsItsHost(string uri, string expected)
    {
        Assert.Equal(expected, SiteName.Display(new Uri(uri)));
    }

    [Theory]
    [InlineData("https://www.example.com/", "example.com")]
    [InlineData("https://WWW.Example.com/", "example.com")]
    public void ALeadingWwwIsDropped(string uri, string expected)
    {
        Assert.Equal(expected, SiteName.Display(new Uri(uri)));
    }

    [Fact]
    public void WwwInTheMiddleOfAHostStays()
    {
        Assert.Equal("a.www.example.com", SiteName.Display(new Uri("https://a.www.example.com/")));
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
}
