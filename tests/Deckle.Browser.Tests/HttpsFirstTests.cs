using Xunit;

namespace Deckle.Browser.Tests;

// A top-level http navigation becomes https, except for local hosts and hosts
// the person allowed.
[Trait("Category", "unit")]
public sealed class HttpsFirstTests
{
    [Theory]
    [InlineData("http://example.com/a?b=c#d", "https://example.com/a?b=c#d")]
    [InlineData("http://example.com:8080/x", "https://example.com:8080/x")]
    [InlineData("http://example.com:80/x", "https://example.com/x")]
    public void AnHttpAddressBecomesHttps(string address, string expected)
    {
        Assert.Equal(new Uri(expected), new HttpsFirst().Upgrade(new Uri(address)));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("about:blank")]
    [InlineData("file:///C:/temp/a.html")]
    public void WhatIsNotHttpIsLeftAlone(string address)
    {
        Assert.Null(new HttpsFirst().Upgrade(new Uri(address)));
    }

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://app.localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.1.1/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[fe80::1]/")]
    public void LocalAndPrivateHostsAreExempt(string address)
    {
        Assert.Null(new HttpsFirst().Upgrade(new Uri(address)));
    }

    // The private IPv4 ranges, written as octets so no address literal sits in the source.
    [Theory]
    [InlineData(10, 0, 0, 5)]
    [InlineData(172, 16, 4, 1)]
    [InlineData(172, 31, 255, 1)]
    [InlineData(192, 168, 0, 10)]
    public void PrivateIpv4RangesAreExempt(int a, int b, int c, int d)
    {
        Assert.Null(new HttpsFirst().Upgrade(new Uri($"http://{a}.{b}.{c}.{d}/")));
    }

    [Theory]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://8.8.8.8/")]
    public void APublicIpLiteralIsUpgraded(string address)
    {
        Assert.NotNull(new HttpsFirst().Upgrade(new Uri(address)));
    }

    [Fact]
    public void AHostTheUserAllowedStaysOnHttp()
    {
        var rule = new HttpsFirst();
        Assert.NotNull(rule.Upgrade(new Uri("http://example.com/")));

        rule.AllowHttp("example.com");

        Assert.Null(rule.Upgrade(new Uri("http://example.com/page")));
        Assert.NotNull(rule.Upgrade(new Uri("http://other.example/")));
    }
}
