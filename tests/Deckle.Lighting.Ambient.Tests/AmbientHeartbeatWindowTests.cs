using Deckle.Lighting.Ambient;
using Xunit;

namespace Deckle.Lighting.Ambient.Tests;

[Trait("Category", "unit")]
public sealed class AmbientHeartbeatWindowTests
{
    [Fact]
    public void ContinuedAdmissionKeepsTheCurrentObservationWindow()
    {
        var window = new AmbientHeartbeatWindow();

        Assert.True(window.StartIfNeeded(1_000));
        Assert.False(window.StartIfNeeded(4_000));

        Assert.Equal(4_000, window.ElapsedTicks(5_000));
    }

    [Fact]
    public void ReadmissionStartsASeparateObservationWindow()
    {
        var window = new AmbientHeartbeatWindow();
        window.StartIfNeeded(1_000);
        window.Stop();

        Assert.True(window.StartIfNeeded(11_000));

        Assert.Equal(0, window.ElapsedTicks(11_000));
        Assert.Equal(5_000, window.ElapsedTicks(16_000));
    }
}
