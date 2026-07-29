using Deckle.Input;
using Xunit;

namespace Deckle.Input.Tests;

[Trait("Category", "unit")]
public sealed class MouseWheelTimestampTests
{
    [Fact]
    public void CallbackDelayPreservesTheSourceEventTime()
    {
        double translated = MouseWheelTimestamp.ToSharedClock(
            eventTime: 1_000,
            currentTick: 1_025,
            currentSharedMs: 500);

        Assert.Equal(475, translated);
    }

    [Fact]
    public void NativeTickWrapPreservesTheElapsedGap()
    {
        double translated = MouseWheelTimestamp.ToSharedClock(
            eventTime: 0xFFFFFFF0,
            currentTick: 0x00000010,
            currentSharedMs: 500);

        Assert.Equal(468, translated);
    }
}
