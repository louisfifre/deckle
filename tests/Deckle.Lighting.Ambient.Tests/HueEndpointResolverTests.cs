using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class HueEndpointResolverTests
{
    private static readonly HueBridge BridgeA = new("bridge-a", "192.168.1.10", 443);
    private static readonly HueBridge BridgeB = new("bridge-b", "192.168.1.11", 443);

    [Fact]
    public async Task FindsCanonicalBridgeByIdentity()
    {
        var result = await HueEndpointResolver.FindAsync(
            "BRIDGE-A",
            [BridgeA, BridgeB],
            (bridge, _) => Task.FromResult(bridge == BridgeA),
            CancellationToken.None);

        Assert.Equal(BridgeA, result.Bridge);
        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Valid);
    }

    [Fact]
    public async Task MigratesManualIdentityOnlyWithOneValidBridge()
    {
        var result = await HueEndpointResolver.FindAsync(
            HuePairingService.ManualBridgeId,
            [BridgeA, BridgeB],
            (bridge, _) => Task.FromResult(bridge == BridgeB),
            CancellationToken.None);

        Assert.Equal(BridgeB, result.Bridge);
        Assert.Equal(2, result.Candidates);
        Assert.Equal(1, result.Valid);
    }

    [Fact]
    public async Task RejectsAmbiguousManualIdentity()
    {
        var result = await HueEndpointResolver.FindAsync(
            HuePairingService.ManualBridgeId,
            [BridgeA, BridgeB],
            (_, _) => Task.FromResult(true),
            CancellationToken.None);

        Assert.Null(result.Bridge);
        Assert.Equal(2, result.Valid);
    }
}
