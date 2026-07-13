using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Tests;

[Trait("Category", "unit")]
public sealed class HueLocalDiscoveryTests
{
    [Fact]
    public void CreatesBridgeFromPrivateIpv4Advertisement()
    {
        var bridge = HueLocalDiscovery.TryCreateBridge(
            "001788FFFE3A2C18", [192, 168, 1, 11], 443);

        Assert.Equal("001788FFFE3A2C18", bridge?.Id);
        Assert.Equal("192.168.1.11", bridge?.InternalIpAddress);
        Assert.Equal(443, bridge?.Port);
    }

    [Fact]
    public void UsesHueHttpsPortWhenAdvertisementOmitsPort()
    {
        var bridge = HueLocalDiscovery.TryCreateBridge(
            "001788FFFE3A2C18", [10, 0, 0, 4], 0);

        Assert.Equal(443, bridge?.Port);
    }

    [Theory]
    [InlineData(null, 192, 168, 1, 11)]
    [InlineData("", 192, 168, 1, 11)]
    [InlineData("001788FFFE3A2C18", 8, 8, 8, 8)]
    public void RejectsAdvertisementWithoutLocalHueIdentity(
        string? bridgeId, byte a, byte b, byte c, byte d)
    {
        var bridge = HueLocalDiscovery.TryCreateBridge(bridgeId, [a, b, c, d], 443);

        Assert.Null(bridge);
    }
}
