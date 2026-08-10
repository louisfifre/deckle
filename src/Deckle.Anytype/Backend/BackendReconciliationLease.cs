using System.Diagnostics;

namespace Deckle.Anytype;

internal interface IBackendReconciliationCoordinator
{
    Task<BackendReconciliationResult> RunAsync(
        string reconciliationId,
        Func<bool, BackendReconciliationResult> action,
        CancellationToken ct);
}

// Mutex ownership is thread-affine. The complete protected action runs on one
// dedicated worker and may synchronously bridge cancellable async I/O; acquiring
// on one pool thread and releasing after an await on another is never allowed.
internal sealed class BackendReconciliationLease : IBackendReconciliationCoordinator
{
    private const string DefaultMutexName = "Deckle.Anytype.Backend";
    private static readonly NamedWaitHandleOptions Options = new()
    {
        CurrentUserOnly = true,
        CurrentSessionOnly = false,
    };
    private readonly string _mutexName;
    private readonly Action? _waitStarted;

    internal BackendReconciliationLease(string? mutexName = null, Action? waitStarted = null)
    {
        _mutexName = mutexName ?? DefaultMutexName;
        _waitStarted = waitStarted;
    }

    public Task<BackendReconciliationResult> RunAsync(
        string reconciliationId,
        Func<bool, BackendReconciliationResult> action,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Task.Factory.StartNew(
            () => RunOnOwningThread(_mutexName, reconciliationId, action, ct, _waitStarted),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static BackendReconciliationResult RunOnOwningThread(
        string mutexName,
        string reconciliationId,
        Func<bool, BackendReconciliationResult> action,
        CancellationToken ct,
        Action? waitStarted)
    {
        using var mutex = new Mutex(false, mutexName, Options);
        bool acquired = false;
        bool abandoned = false;
        long started = Stopwatch.GetTimestamp();
        try
        {
            try
            {
                waitStarted?.Invoke();
                int signaled = WaitHandle.WaitAny([mutex, ct.WaitHandle]);
                if (signaled != 0) throw new OperationCanceledException(ct);
                acquired = true;
            }
            catch (AbandonedMutexException)
            {
                // Windows grants ownership to this thread. No protected state is
                // trusted: the reconciler re-reads processes and the TCP table.
                acquired = true;
                abandoned = true;
            }

            DeckleAnytypeSource.Log.BackendReconciliationLeaseAcquired(
                reconciliationId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                abandoned);
            return action(abandoned);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
