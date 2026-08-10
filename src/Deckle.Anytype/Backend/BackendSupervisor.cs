namespace Deckle.Anytype;

public enum BackendStartOutcome
{
    AlreadyRunning,
    Started,
    NotProvisioned,
    StartRejected,
    TimedOut,
    EndpointConflict,
}

// Owns one initial reconciliation and the process-exit watch that follows it.
// Disposal cancels and drains both operations; it never terminates the backend.
public sealed class BackendSupervisor : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan[] RestartBackoff =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60)];
    private static readonly TimeSpan StableUptime = TimeSpan.FromMinutes(5);

    private readonly IBackendReconciler _reconciler;
    private readonly IBackendHealthProbe _health;
    private readonly IBackendTime _time;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _gate = new();
    private Task<BackendStartOutcome>? _initializationTask;
    private Task? _watchTask;
    private Task? _disposalTask;
    private bool _disposed;

    public BackendSupervisor(BackendHealthProbe health)
        : this(BackendReconciler.CreateDefault(health), health, new BackendTime())
    {
    }

    internal BackendSupervisor(
        IBackendReconciler reconciler,
        IBackendHealthProbe health,
        IBackendTime time)
    {
        _reconciler = reconciler;
        _health = health;
        _time = time;
    }

    public Task<BackendStartOutcome> EnsureRunningAsync(CancellationToken ct = default)
    {
        Task<BackendStartOutcome> initialization;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            initialization = _initializationTask ??= InitializeAsync(_lifetimeCts.Token);
        }

        // Initialization belongs to the supervisor, not to whichever caller
        // happened to arrive first. A caller may abandon only its own wait;
        // disposal remains the sole cancellation boundary for reconciliation.
        return ct.CanBeCanceled ? initialization.WaitAsync(ct) : initialization;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposalTask is not null)
                return new ValueTask(_disposalTask);

            _disposed = true;
            _lifetimeCts.Cancel();
            _disposalTask = DisposeCoreAsync(_initializationTask);
            return new ValueTask(_disposalTask);
        }
    }

    private async Task DisposeCoreAsync(Task<BackendStartOutcome>? initialization)
    {
        Task? watch;
        try
        {
            await IgnoreCancellation(initialization).ConfigureAwait(false);
            lock (_gate) watch = _watchTask;
            await IgnoreCancellation(watch).ConfigureAwait(false);
        }
        finally
        {
            // A reconciliation or watch fault must not bypass ownership cleanup.
            _health.Dispose();
            _lifetimeCts.Dispose();
        }
    }

    private async Task<BackendStartOutcome> InitializeAsync(CancellationToken lifetimeToken)
    {
        BackendReconciliationResult result = await _reconciler
            .ReconcileAsync("startup", lifetimeToken)
            .ConfigureAwait(false);
        if (result.Process is not null)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    result.Process.Dispose();
                    return result.Outcome;
                }
                _watchTask = WatchAsync(result.Process, _lifetimeCts.Token);
            }
        }
        else if (result.Outcome is BackendStartOutcome.StartRejected or BackendStartOutcome.TimedOut)
        {
            lock (_gate)
            {
                if (!_disposed)
                    _watchTask = RecoverAsync(attempt: 0, _lifetimeCts.Token);
            }
        }
        return result.Outcome;
    }

    private async Task RecoverAsync(int attempt, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                TimeSpan delay = RestartBackoff[Math.Min(attempt, RestartBackoff.Length - 1)];
                DeckleAnytypeSource.Log.BackendRestartScheduled(attempt + 1, delay.TotalMilliseconds);
                await Task.Run(() => _time.Delay(delay, ct), ct).ConfigureAwait(false);

                BackendReconciliationResult result = await _reconciler
                    .ReconcileAsync("process-exit", ct)
                    .ConfigureAwait(false);
                if (result.Process is not null)
                {
                    await WatchAsync(result.Process, ct).ConfigureAwait(false);
                    return;
                }
                if (result.Outcome is BackendStartOutcome.NotProvisioned or BackendStartOutcome.EndpointConflict)
                    return;
                attempt = Math.Min(attempt + 1, RestartBackoff.Length - 1);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task WatchAsync(IBackendProcess process, CancellationToken ct)
    {
        IBackendProcess? current = process;
        int attempt = 0;
        try
        {
            while (current is not null)
            {
                await current.WaitForExitAsync(ct).ConfigureAwait(false);
                double uptimeSeconds = Math.Max(
                    0, (DateTimeOffset.UtcNow - current.StartedAt).TotalSeconds);
                DeckleAnytypeSource.Log.BackendStopped();
                DeckleAnytypeSource.Log.BackendStoppedDetail(
                    current.Id, current.ExitCode, uptimeSeconds);
                current.Dispose();
                current = null;

                attempt = uptimeSeconds >= StableUptime.TotalSeconds ? 0 : attempt;
                while (current is null)
                {
                    TimeSpan delay = RestartBackoff[Math.Min(attempt, RestartBackoff.Length - 1)];
                    DeckleAnytypeSource.Log.BackendRestartScheduled(attempt + 1, delay.TotalMilliseconds);
                    await Task.Run(() => _time.Delay(delay, ct), ct).ConfigureAwait(false);

                    BackendReconciliationResult result = await _reconciler
                        .ReconcileAsync("process-exit", ct)
                        .ConfigureAwait(false);
                    current = result.Process;
                    if (current is null && result.Outcome is
                        BackendStartOutcome.NotProvisioned or BackendStartOutcome.EndpointConflict)
                        return;
                    attempt = Math.Min(attempt + 1, RestartBackoff.Length - 1);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Deckle is leaving. The process is intentionally left alive.
        }
        finally
        {
            current?.Dispose();
        }
    }

    private static async Task IgnoreCancellation(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
