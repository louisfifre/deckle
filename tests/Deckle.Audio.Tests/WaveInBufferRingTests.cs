using Deckle.Audio;
using Xunit;

namespace Deckle.Audio.Tests;

[Trait("Category", "unit")]
public sealed class WaveInBufferRingTests
{
    // Takes every buffer in turn until the ring stops at one that is not done.
    private static List<int> TakeAllDone(ref WaveInBufferRing ring, int count, Func<int, bool> isDone)
    {
        var taken = new List<int>();
        while (taken.Count < count && ring.TakeNext(isDone, out int index))
            taken.Add(index);
        return taken;
    }

    [Fact]
    public void BuffersComeBackInTheOrderTheyWereQueued()
    {
        var ring = new WaveInBufferRing(4);

        Assert.Equal([0, 1, 2], TakeAllDone(ref ring, 4, i => i < 3));
    }

    // 2026-10-21: a scan by index appended buffer 0 before buffer 3 whenever
    // both were done at one wake, which swapped two 50 ms chunks of the take.
    [Fact]
    [Trait("Category", "regression")]
    public void BuffersAcrossTheWrapAreTakenOldestFirst()
    {
        var ring = new WaveInBufferRing(4);
        TakeAllDone(ref ring, 4, i => i < 3);
        Assert.Equal(3, ring.Oldest);

        // Buffer 3 was queued before buffer 0 was requeued, so it is older.
        Assert.Equal([3, 0], TakeAllDone(ref ring, 4, i => i == 3 || i == 0));
        Assert.Equal(1, ring.Oldest);
    }

    [Fact]
    public void TakingStopsAtTheFirstBufferThatIsNotDone()
    {
        var ring = new WaveInBufferRing(4);

        // Buffer 2 is not done, so buffer 3 cannot be taken ahead of it.
        Assert.Equal([0, 1], TakeAllDone(ref ring, 4, i => i != 2));
        Assert.Equal(2, ring.Oldest);
    }

    [Fact]
    public void ANotDoneBufferIsLeftForTheNextWake()
    {
        var ring = new WaveInBufferRing(4);

        Assert.Empty(TakeAllDone(ref ring, 4, _ => false));
        Assert.Equal(0, ring.Oldest);
        Assert.Equal([0, 1, 2, 3], TakeAllDone(ref ring, 4, _ => true));
    }

    [Fact]
    public void ARingWithOneBufferTakesItAgainEachLap()
    {
        var ring = new WaveInBufferRing(1);

        Assert.Equal([0], TakeAllDone(ref ring, 1, _ => true));
        Assert.Equal([0], TakeAllDone(ref ring, 1, _ => true));
    }
}
