using Deckle.Input.PrecisionScroll;
using Xunit;

namespace Deckle.Input.PrecisionScroll.Tests;

[Trait("Category", "unit")]
public sealed class WheelTickQueueTests
{
    [Fact]
    public void FullQueueRejectsOnlyTheUnacceptedTick()
    {
        var queue = new WheelTickQueue();

        for (int index = 0; index < WheelTickQueue.Capacity; index++)
            Assert.True(queue.TryEnqueue(new WheelTick(index + 1, index)));

        Assert.False(queue.TryEnqueue(new WheelTick(999, 999)));

        for (int index = 0; index < WheelTickQueue.Capacity; index++)
        {
            Assert.True(queue.TryDequeue(out WheelTick accepted));
            Assert.Equal(index + 1, accepted.Detents);
            Assert.Equal(index, accepted.TimestampMs);
        }

        Assert.False(queue.TryDequeue(out _));
    }
}
