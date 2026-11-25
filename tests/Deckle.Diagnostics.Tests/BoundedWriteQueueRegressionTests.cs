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

    [Fact]
    public async Task A_full_log_queue_drops_new_lines_without_blocking_and_reports_the_count_once_writing_resumes()
    {
        using var diskReleased = new ManualResetEventSlim();
        using var writeStarted = new ManualResetEventSlim();
        var written = new List<string>();
        var reports = new List<int>();
        using var queue = new BoundedWriteQueue<string>(
            capacity: 1,
            write: line =>
            {
                writeStarted.Set();
                diskReleased.Wait();
                lock (written) written.Add(line);
                return ValueTask.CompletedTask;
            },
            flush: () => ValueTask.CompletedTask,
            close: () => { },
            dropWhenFull: true,
            onDropped: lost => { lock (reports) reports.Add(lost); });

        try
        {
            queue.Enqueue("stuck in the write");
            Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5)));
            queue.Enqueue("fills the channel");

            Task burst = Task.Run(() =>
            {
                for (int i = 0; i < 5; i++) queue.Enqueue($"lost {i}");
            });
            Assert.True(await Task.WhenAny(burst, Task.Delay(TimeSpan.FromSeconds(5))) == burst,
                "A producer waited on a full log queue instead of dropping its line.");
            lock (reports) Assert.Empty(reports);
        }
        finally
        {
            diskReleased.Set();
        }

        Assert.True(queue.TryFlush(TimeSpan.FromSeconds(5)));
        SpinWait.SpinUntil(() => { lock (reports) return reports.Count > 0; }, TimeSpan.FromSeconds(5));
        Thread.Sleep(100);

        lock (written) Assert.Equal(["stuck in the write", "fills the channel"], written);
        lock (reports) Assert.Equal([5], reports);
    }

    [Fact]
    public async Task A_dataset_queue_still_waits_on_a_full_queue_and_loses_nothing()
    {
        using var diskReleased = new ManualResetEventSlim();
        using var writeStarted = new ManualResetEventSlim();
        var written = new List<string>();
        using var queue = new BoundedWriteQueue<string>(
            capacity: 1,
            write: line =>
            {
                writeStarted.Set();
                diskReleased.Wait();
                lock (written) written.Add(line);
                return ValueTask.CompletedTask;
            },
            flush: () => ValueTask.CompletedTask,
            close: () => { });

        queue.Enqueue("first");
        Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5)));
        queue.Enqueue("second");
        Task waiting = Task.Run(() => queue.Enqueue("third"));

        await Task.Delay(200);
        Assert.False(waiting.IsCompleted, "The producer did not wait for room.");

        diskReleased.Set();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.TryFlush(TimeSpan.FromSeconds(5)));

        lock (written) Assert.Equal(["first", "second", "third"], written);
    }
}
