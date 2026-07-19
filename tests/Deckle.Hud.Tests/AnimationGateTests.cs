using Xunit;

namespace Deckle.Hud.Tests;

public sealed class AnimationGateTests
{
    [Fact]
    public void Disable_SignalsSnapOnce_AndEnableDoesNotReplay()
    {
        var gate = new AnimationGate(isEnabled: true);
        int snapCount = 0;
        gate.Disabled += () => snapCount++;

        Assert.True(gate.SetEnabled(false));
        Assert.False(gate.SetEnabled(false));
        Assert.Equal(1, snapCount);

        Assert.True(gate.SetEnabled(true));
        Assert.Equal(1, snapCount);
    }
}
