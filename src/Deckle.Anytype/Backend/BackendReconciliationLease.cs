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
    // Longer than the 20 s readiness window a healthy holder may legitimately
    // keep the lease for, so the warning means a stuck holder, not a busy one.
    private static readonly TimeSpan LeaseWaitWarning = TimeSpan.FromSeconds(30);
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
        bool warned = false;
        long started = Stopwatch.GetTimestamp();
        try
        {
            try
            {
                waitStarted?.Invoke();
                WaitHandle[] handles = [mutex, ct.WaitHandle];
                int signaled = WaitHandle.WaitAny(handles, LeaseWaitWarning);
                if (signaled == WaitHandle.WaitTimeout)
                {
                    // The wait stays unbounded (a second serve is worse than a
                    // late one); only the silence ends here.
                    warned = true;
                    DeckleAnytypeSource.Log.BackendReconciliationLeaseWaiting(
                        reconciliationId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    signaled = WaitHandle.WaitAny(handles);
                }
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

            double waitedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            DeckleAnytypeSource.Log.BackendReconciliationLeaseAcquired(
                reconciliationId, waitedMs, abandoned);
            if (warned)
                DeckleAnytypeSource.Log.BackendReconciliationLeaseWaitEnded(
                    reconciliationId, waitedMs, abandoned);
            return action(abandoned);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
