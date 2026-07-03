using Deckle.Lighting;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientPushGateTests
{
    [Fact]
    public void PersistentOutputsDropSmallRepeatedChanges()
    {
        bool dropped = AmbientPushGate.ShouldDrop(
            new LightColor(11, 10, 10),
            (10, 10, 10),
            threshold: 6,
            requiresContinuousColorUpdates: false);

        Assert.True(dropped);
    }

    [Fact]
    public void ContinuousOutputsKeepSmallRepeatedChangesAlive()
    {
        bool dropped = AmbientPushGate.ShouldDrop(
            new LightColor(10, 10, 10),
            (10, 10, 10),
            threshold: 6,
            requiresContinuousColorUpdates: true);

        Assert.False(dropped);
    }
}
