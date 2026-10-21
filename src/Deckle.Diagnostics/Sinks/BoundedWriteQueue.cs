using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace Deckle.Diagnostics;

// One bounded, ordered hand-off from EventSource emitter threads to a sink's
// asynchronous writer. If producers outrun disk, WriteAsync applies
// backpressure instead of dropping a row or growing without bound. A flush
// marker is ordered with data, making Flush deterministic.
//
// Each sink owns a lightweight pending Task, not a dedicated OS thread. Actual
// file writes use asynchronous I/O, so several dormant JSONL destinations do
// not reserve a thread apiece.
//
// Write failures. A write that throws on the worker is remembered as the last
// error, which Flush and Dispose rethrow, and the item is lost. That alone
// hides a dead destination until exit, so the queue also keeps one incident:
// the first failed write after successes calls onFault, the first successful
// write after failures calls onRecovered with the count. Both callbacks run
// on a thread-pool thread, never on the worker: a callback that emits an
// event dispatches back into this very sink, and a worker that enqueues into
// its own full channel waits on itself forever.
internal sealed class BoundedWriteQueue<T> : IDisposable where T : class
{
    private readonly Channel<WorkItem> _items;
    private readonly Func<T, ValueTask> _write;
    private readonly Func<ValueTask> _flush;
    private readonly Action _close;
    private readonly Action<Exception>? _onFault;
    private readonly Action<int>? _onRecovered;
    private readonly Task _worker;
    private readonly object _lifetimeLock = new();

    private Exception? _lastError;
    private bool _accepting = true;
    private bool _disposed;

    // Worker-owned incident state.
    private int _consecutiveFailures;

    public BoundedWriteQueue(
        int capacity,
        Func<T, ValueTask> write,
        Func<ValueTask> flush,
        Action close,
        Action<Exception>? onFault = null,
        Action<int>? onRecovered = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        _items = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _flush = flush ?? throw new ArgumentNullException(nameof(flush));
        _close = close ?? throw new ArgumentNullException(nameof(close));
        _onFault = onFault;
        _onRecovered = onRecovered;
        _worker = Task.Run(ConsumeAsync);
    }

    public void Enqueue(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // The lock makes completion atomic with the last accepted write. It is
        // uncontended and non-blocking while capacity remains; only sustained
        // disk pressure deliberately propagates back to producers.
        lock (_lifetimeLock)
        {
            if (!_accepting) return;
            _items.Writer.WriteAsync(WorkItem.ForValue(value))
                .AsTask().GetAwaiter().GetResult();
        }
    }

    public void Flush()
    {
        TryFlush(Timeout.InfiniteTimeSpan);
    }

    // False when the deadline passed before the marker was reached: the
    // entries stay queued and the worker keeps draining them. A deadline is
    // also applied to the enqueue of the marker itself, since a full channel
    // behind a stuck disk would otherwise block the caller before any wait.
    public bool TryFlush(TimeSpan timeout)
    {
        var completed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool markerQueued;

        lock (_lifetimeLock)
        {
            markerQueued = _accepting;
            if (markerQueued)
            {
                ValueTask enqueue = _items.Writer.WriteAsync(WorkItem.ForFlush(completed));
                if (!enqueue.IsCompleted
                    && !enqueue.AsTask().Wait(timeout))
                {
                    return false;
                }
            }
        }

        bool drained = markerQueued
            ? completed.Task.Wait(timeout)
            : _worker.Wait(timeout);

        ThrowLastError();
        return drained;
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (!_disposed)
            {
                _disposed = true;
                _accepting = false;
                _items.Writer.TryComplete();
            }
        }

        _worker.GetAwaiter().GetResult();
        ThrowLastError();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (WorkItem item in _items.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (item.FlushCompleted is not null)
                {
                    await TryRunAsync(_flush).ConfigureAwait(false);
                    item.FlushCompleted.TrySetResult(true);
                    continue;
                }

                await WriteTrackedAsync(item.Value!).ConfigureAwait(false);
            }

            await TryRunAsync(_flush).ConfigureAwait(false);
        }
        finally
        {
            try { _close(); }
            catch (Exception ex) { Interlocked.Exchange(ref _lastError, ex); }
        }
    }

    private async ValueTask WriteTrackedAsync(T value)
    {
        try
        {
            await _write(value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _lastError, ex);
            if (_consecutiveFailures++ == 0 && _onFault is not null)
            {
                Action<Exception> onFault = _onFault;
                _ = Task.Run(() => onFault(ex));
            }
            return;
        }

        if (_consecutiveFailures > 0)
        {
            int failures = _consecutiveFailures;
            _consecutiveFailures = 0;
            if (_onRecovered is not null)
            {
                Action<int> onRecovered = _onRecovered;
                _ = Task.Run(() => onRecovered(failures));
            }
        }
    }

    private async ValueTask TryRunAsync(Func<ValueTask> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception ex) { Interlocked.Exchange(ref _lastError, ex); }
    }

    private void ThrowLastError()
    {
        Exception? error = Volatile.Read(ref _lastError);
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed record WorkItem(T? Value, TaskCompletionSource<bool>? FlushCompleted)
    {
        public static WorkItem ForValue(T value) => new(value, null);
        public static WorkItem ForFlush(TaskCompletionSource<bool> completed) => new(null, completed);
    }
}
