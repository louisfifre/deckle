using System.Collections.Concurrent;
using System.Diagnostics;

namespace Deckle.Autocorrect.Probe;

internal sealed record QwenProcessResourceSample(
    double OffsetMilliseconds,
    long PrivateMemoryBytes,
    long WorkingSetBytes,
    long ManagedHeapBytes,
    double ProcessCpuMilliseconds);

internal sealed record QwenResourceTransition(
    string Name,
    string CacheState,
    IReadOnlyList<QwenProcessResourceSample> RawSamples,
    double OperationWallMilliseconds,
    long PrivateMemoryEndpointDeltaBytes,
    long WorkingSetEndpointDeltaBytes,
    long ManagedHeapEndpointDeltaBytes,
    long PrivateMemoryObservedPeakDeltaBytes,
    long WorkingSetObservedPeakDeltaBytes,
    long ManagedHeapObservedPeakDeltaBytes,
    double ProcessCpuDeltaMilliseconds,
    long CurrentThreadAllocatedBytes);

internal sealed record QwenMeasured<T>(T Value, QwenResourceTransition Transition);

internal static class QwenAdapterResourceSampler
{
    private const int SamplePeriodMilliseconds = 50;
    private const int QuiescenceMilliseconds = 250;

    public static QwenMeasured<T> Measure<T>(string name, Func<T> transition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(transition);

        CollectFullGarbage();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long started = Stopwatch.GetTimestamp();
        var samples = new ConcurrentQueue<QwenProcessResourceSample>();
        samples.Enqueue(Snapshot(process, started));
        using var cancellation = new CancellationTokenSource();
        Task sampler = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        SamplePeriodMilliseconds,
                        cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                samples.Enqueue(Snapshot(process, started));
            }
        });

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        T value;
        long allocatedAfter;
        double operationWallMilliseconds;
        try
        {
            value = transition();
            allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            operationWallMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Thread.Sleep(QuiescenceMilliseconds);
            samples.Enqueue(Snapshot(process, started));
        }
        finally
        {
            cancellation.Cancel();
            sampler.GetAwaiter().GetResult();
        }

        QwenProcessResourceSample[] raw = samples.ToArray();
        QwenProcessResourceSample first = raw[0];
        QwenProcessResourceSample last = raw[^1];
        return new QwenMeasured<T>(
            value,
            new QwenResourceTransition(
                name,
                "os_file_cache_not_flushed",
                raw,
                operationWallMilliseconds,
                last.PrivateMemoryBytes - first.PrivateMemoryBytes,
                last.WorkingSetBytes - first.WorkingSetBytes,
                last.ManagedHeapBytes - first.ManagedHeapBytes,
                raw.Max(static sample => sample.PrivateMemoryBytes)
                    - first.PrivateMemoryBytes,
                raw.Max(static sample => sample.WorkingSetBytes)
                    - first.WorkingSetBytes,
                raw.Max(static sample => sample.ManagedHeapBytes)
                    - first.ManagedHeapBytes,
                last.ProcessCpuMilliseconds - first.ProcessCpuMilliseconds,
                allocatedAfter - allocatedBefore));
    }

    public static void CollectFullGarbage()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static QwenProcessResourceSample Snapshot(Process process, long started)
    {
        process.Refresh();
        return new QwenProcessResourceSample(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            process.PrivateMemorySize64,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            process.TotalProcessorTime.TotalMilliseconds);
    }
}
