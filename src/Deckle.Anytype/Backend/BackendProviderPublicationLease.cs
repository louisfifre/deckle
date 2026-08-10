namespace Deckle.Anytype;

internal interface IBackendProviderPublicationCoordinator
{
    void Run(Action action, CancellationToken ct);
}

// Publishing a ready version and switching activation form one cross-process
// transaction. Staging remains concurrent and private; only the short filesystem
// commit is serialized for the current user across Windows sessions.
internal sealed class BackendProviderPublicationLease(string? mutexName = null)
    : IBackendProviderPublicationCoordinator
{
    private const string DefaultMutexName = "Deckle.Anytype.ProviderPublication";
    private static readonly NamedWaitHandleOptions Options = new()
    {
        CurrentUserOnly = true,
        CurrentSessionOnly = false,
    };
    private readonly string _mutexName = mutexName ?? DefaultMutexName;

    public void Run(Action action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var mutex = new Mutex(false, _mutexName, Options);
        bool acquired = false;
        try
        {
            try
            {
                int signaled = WaitHandle.WaitAny([mutex, ct.WaitHandle]);
                if (signaled != 0) throw new OperationCanceledException(ct);
                acquired = true;
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            ct.ThrowIfCancellationRequested();
            action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
