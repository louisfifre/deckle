using Xunit;

namespace Deckle.Browser.Tests;

// What the address field does with what the user typed.
[Trait("Category", "unit")]
public sealed class AddressInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void NothingTypedOpensNothing(string typed)
    {
        Assert.Null(AddressInput.Resolve(typed));
    }

    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("http://example.com/", "http://example.com/")]
    [InlineData("file:///C:/temp/page.html", "file:///C:/temp/page.html")]
    [InlineData("about:blank", "about:blank")]
    public void AnAddressWithAnOpenSchemeIsKept(string typed, string expected)
    {
        Assert.Equal(new Uri(expected), AddressInput.Resolve(typed));
    }

    [Fact]
    public void TheTextIsTrimmedBeforeItIsRead()
    {
        Assert.Equal(new Uri("https://example.com/"), AddressInput.Resolve("  example.com \n"));
    }

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("www.example.com/path?q=1", "https://www.example.com/path?q=1")]
    [InlineData("example.com:8080/x", "https://example.com:8080/x")]
    [InlineData("localhost", "https://localhost/")]
    [InlineData("localhost:5000/app", "https://localhost:5000/app")]
    public void AHostOpensOverHttps(string typed, string expected)
    {
        Assert.Equal(new Uri(expected), AddressInput.Resolve(typed));
    }

    [Theory]
    [InlineData("hello world", "https://duckduckgo.com/?q=hello%20world")]
    [InlineData("weather", "https://duckduckgo.com/?q=weather")]
    [InlineData("what is example.com", "https://duckduckgo.com/?q=what%20is%20example.com")]
    public void AnythingElseIsASearch(string typed, string expected)
    {
        Assert.Equal(new Uri(expected), AddressInput.Resolve(typed));
    }

    [Fact]
    public void TheSearchEscapesWhatWouldBreakTheQuery()
    {
        Uri? uri = AddressInput.Resolve("a&b=c d");

        Assert.NotNull(uri);
        Assert.Equal("?q=a%26b%3Dc%20d", uri.Query);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com")]
    public void AnUnlistedSchemeIsSearchedNotOpened(string typed)
    {
        Uri? uri = AddressInput.Resolve(typed);

        Assert.NotNull(uri);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("duckduckgo.com", uri.Host);
    }

    [Fact]
    public void TheSearchTemplateTakesTheEscapedTextAtItsPlaceholder()
    {
        Assert.Contains("{0}", AddressInput.SearchTemplate);
    }
}
