using Xunit;

namespace Deckle.Browser.Tests;

// The registrable domain (public suffix plus one label), from the embedded
// Public Suffix List.
[Trait("Category", "unit")]
public sealed class RegistrableDomainTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("www.example.com", "example.com")]
    [InlineData("a.b.c.example.org", "example.org")]
    public void ASimpleSuffixKeepsOneLabelBeforeIt(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.Of(host));
    }

    [Theory]
    [InlineData("mail.example.co.uk", "example.co.uk")]
    [InlineData("example.co.uk", "example.co.uk")]
    [InlineData("a.example.com.au", "example.com.au")]
    public void AMultiPartSuffixIsOneSuffix(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.Of(host));
    }

    // *.ck makes "bar.ck" a suffix; !www.ck makes "www.ck" a registrable name.
    [Theory]
    [InlineData("foo.bar.ck", "foo.bar.ck")]
    [InlineData("a.foo.bar.ck", "foo.bar.ck")]
    [InlineData("www.ck", "www.ck")]
    [InlineData("a.www.ck", "www.ck")]
    public void WildcardAndExceptionRulesApply(string host, string expected)
    {
        Assert.Equal(expected, RegistrableDomain.Of(host));
    }

    [Fact]
    public void ThePrivateSectionCounts()
    {
        Assert.Equal("me.github.io", RegistrableDomain.Of("me.github.io"));
        Assert.Equal("me.github.io", RegistrableDomain.Of("www.me.github.io"));
    }

    [Fact]
    public void ABareSuffixComesBackAsItIs()
    {
        Assert.Equal("co.uk", RegistrableDomain.Of("co.uk"));
        Assert.Equal("com", RegistrableDomain.Of("com"));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void AnIpLiteralOrLocalhostReturnsItself(string host)
    {
        Assert.Equal(host.Trim('[', ']'), RegistrableDomain.Of(host));
    }

    [Fact]
    public void APrivateIpLiteralReturnsItself()
    {
        string ip = string.Join('.', 192, 168, 1, 20);
        Assert.Equal(ip, RegistrableDomain.Of(ip));
    }

    [Fact]
    public void CaseAndATrailingDotDoNotMatter()
    {
        Assert.Equal("example.co.uk", RegistrableDomain.Of("WWW.Example.CO.UK."));
    }

    [Fact]
    public void AUnicodeHostComesBackInPunycode()
    {
        Assert.Equal("xn--bcher-kva.example", RegistrableDomain.Of("www.bücher.example"));
    }
}
