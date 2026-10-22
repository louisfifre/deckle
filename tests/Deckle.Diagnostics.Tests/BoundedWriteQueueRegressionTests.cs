using Xunit;

namespace Deckle.Diagnostics.Tests;

[Trait("Category", "regression")]
public sealed class BoundedWriteQueueRegressionTests
{
    [Fact]
    public async Task A_bounded_flush_returns_in_time_while_a_producer_waits_on_a_full_queue()
    {
        using var diskReleased = new ManualResetEventSlim();
        using var writeStarted = new ManualResetEventSlim();
        using var queue = new BoundedWriteQueue<string>(
            capacity: 1,
            write: _ =>
            {
                writeStarted.Set();
                diskReleased.Wait();
                return ValueTask.CompletedTask;
            },
            flush: () => ValueTask.CompletedTask,
            close: () => { });

        try
        {
            // The worker is stuck inside the first write; the second entry fills
            // the channel, so the third producer blocks holding the queue's lock.
            queue.Enqueue("stuck in the write");
            Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5)));
            queue.Enqueue("fills the channel");
            Task blockedProducer = Task.Run(() => queue.Enqueue("waits for room"));
            await Task.Delay(200);

            Task<bool> flush = Task.Run(() => queue.TryFlush(TimeSpan.FromMilliseconds(100)));

            Assert.True(await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5))) == flush,
                "TryFlush ignored its deadline and waited for the stuck disk.");
            Assert.False(await flush);
        }
        finally
        {
            diskReleased.Set();
        }
    }
}
