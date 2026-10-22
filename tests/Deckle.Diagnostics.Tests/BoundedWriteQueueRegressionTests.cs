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

    [Fact]
    public void A_write_error_already_reported_as_recovered_is_not_rethrown_at_flush()
    {
        int calls = 0;
        using var queue = new BoundedWriteQueue<string>(
            capacity: 4,
            write: _ =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new IOException("disk hiccup");
                return ValueTask.CompletedTask;
            },
            flush: () => ValueTask.CompletedTask,
            close: () => { });

        queue.Enqueue("lost to the hiccup");
        queue.Enqueue("written after the disk came back");

        Assert.True(queue.TryFlush(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_write_error_with_no_recovery_is_still_rethrown_at_flush()
    {
        var queue = new BoundedWriteQueue<string>(
            capacity: 4,
            write: _ => throw new IOException("disk gone"),
            flush: () => ValueTask.CompletedTask,
            close: () => { });

        queue.Enqueue("lost");

        Assert.Throws<IOException>(() => queue.TryFlush(TimeSpan.FromSeconds(5)));
        Assert.Throws<IOException>(queue.Dispose);
    }
}
