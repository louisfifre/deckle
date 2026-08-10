using Deckle.Anytype;

namespace Deckle.Anytype.Tests;

internal sealed class FakeBackendProvider(string executablePath) : IBackendProviderCatalog
{
    public BackendProcessSpec? ActiveSpec { get; set; } = new(executablePath, "serve");
    public List<string> TrustedPaths { get; } = [executablePath];
    public BackendProcessSpec? ResolveActiveSpec() => ActiveSpec;
    public IReadOnlyList<string> TrustedExecutablePaths() => TrustedPaths;
}

internal sealed class FakeBackendProcess(int id, string executablePath) : IBackendProcess
{
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Id { get; } = id;
    public string ExecutablePath { get; } = executablePath;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool HasExited { get; set; }
    public int ExitCode { get; set; }
    public bool Disposed { get; private set; }

    public Task WaitForExitAsync(CancellationToken ct) => _exited.Task.WaitAsync(ct);
    public void Exit(int code = 0)
    {
        ExitCode = code;
        HasExited = true;
        _exited.TrySetResult();
    }
    public void Dispose() => Disposed = true;
}

internal sealed class FakeBackendProcessHost : IBackendProcessHost
{
    public List<FakeBackendProcess> Running { get; } = [];
    public Dictionary<int, FakeBackendProcess> Opened { get; } = [];
    public FakeBackendProcess? SpawnResult { get; set; }
    public int SpawnCount { get; private set; }

    public IReadOnlyList<IBackendProcess> FindRunning(IReadOnlyCollection<string> executablePaths) =>
        Running.Where(process => !process.HasExited).Cast<IBackendProcess>().ToArray();

    public IBackendProcess? Open(int processId) =>
        Opened.TryGetValue(processId, out FakeBackendProcess? process) ? process : null;

    public IBackendProcess? Spawn(BackendProcessSpec spec)
    {
        SpawnCount++;
        return SpawnResult;
    }
}

internal sealed class ScriptedBackendListener : IBackendListenerOwner
{
    private readonly Queue<BackendListenerSnapshot> _snapshots = new();
    public BackendListenerSnapshot Current { get; set; } = new(BackendListenerState.Unbound);

    public void Enqueue(params BackendListenerSnapshot[] snapshots)
    {
        foreach (BackendListenerSnapshot snapshot in snapshots) _snapshots.Enqueue(snapshot);
    }

    public BackendListenerSnapshot Inspect() =>
        _snapshots.Count > 0 ? _snapshots.Dequeue() : Current;
}

internal sealed class FakeBackendHealth(bool healthy = true) : IBackendHealthProbe
{
    public bool Healthy { get; set; } = healthy;
    public int Calls { get; private set; }
    public bool Disposed { get; private set; }
    public Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Healthy);
    }
    public void Dispose() => Disposed = true;
}

internal sealed class ImmediateBackendCoordinator : IBackendReconciliationCoordinator
{
    public Task<BackendReconciliationResult> RunAsync(
        string reconciliationId,
        Func<bool, BackendReconciliationResult> action,
        CancellationToken ct) => Task.Run(() => action(false), ct);
}

internal sealed class ControlledBackendTime : IBackendTime
{
    private long _milliseconds;
    private int _delayCalls;
    public int DelayCalls => _delayCalls;
    public List<TimeSpan> Delays { get; } = [];
    public TaskCompletionSource? FirstDelayEntered { get; init; }
    public TaskCompletionSource? ReleaseFirstDelay { get; init; }

    public long GetTimestamp() => _milliseconds;
    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        TimeSpan.FromMilliseconds(_milliseconds - startingTimestamp);

    public void Delay(TimeSpan delay, CancellationToken ct)
    {
        lock (Delays) Delays.Add(delay);
        _milliseconds += (long)delay.TotalMilliseconds;
        if (Interlocked.Increment(ref _delayCalls) == 1 && FirstDelayEntered is not null)
        {
            FirstDelayEntered.TrySetResult();
            ReleaseFirstDelay!.Task.WaitAsync(ct).GetAwaiter().GetResult();
        }
        ct.ThrowIfCancellationRequested();
    }
}

internal sealed class SequenceBackendReconciler(params BackendReconciliationResult[] results)
    : IBackendReconciler
{
    private readonly Queue<BackendReconciliationResult> _results = new(results);
    public int Calls { get; private set; }
    public TaskCompletionSource SecondCallEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<BackendReconciliationResult> ReconcileAsync(string trigger, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        if (Calls == 2) SecondCallEntered.TrySetResult();
        return Task.FromResult(_results.Dequeue());
    }
}

internal sealed class BlockingBackendReconciler : IBackendReconciler
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<BackendReconciliationResult> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationObserved { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? ReleaseCancellation { get; init; }
    public int Calls { get; private set; }

    public async Task<BackendReconciliationResult> ReconcileAsync(string trigger, CancellationToken ct)
    {
        Calls++;
        Entered.TrySetResult();
        try
        {
            return await Completion.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CancellationObserved.TrySetResult();
            if (ReleaseCancellation is not null)
                await ReleaseCancellation.Task;
            throw;
        }
    }
}
