using Deckle.Core;

namespace Deckle.Hosting;

// The guard that keeps one copy of an app resident: a named mutex, taken at
// start and held until the process quits. The name comes from the app's
// identity, so the published Deckle keeps Local\Deckle.Resident and every other
// app of the suite has a mutex of its own: two different apps run side by
// side, a second copy of one is refused.
public sealed class ResidentInstance : IDisposable
{
    private Mutex? _mutex;

    private ResidentInstance(Mutex mutex, bool abandoned)
    {
        _mutex = mutex;
        Abandoned = abandoned;
    }

    // True when the previous owner died without orderly teardown: Windows
    // granted this thread ownership, which is safe because no old hook remains.
    // The caller reports it once its log sinks exist.
    public bool Abandoned { get; }

    // Null when this copy must not run: another copy of the same app holds the
    // mutex (failure stays null), or the mutex could not be created or waited
    // on at all (failure names the exception type). Both refuse the launch:
    // closing is safe, while continuing could run two keyboard hooks.
    public static ResidentInstance? TryAcquire(AppIdentity identity, out string? failure) =>
        TryAcquire(() => new Mutex(initiallyOwned: false, identity.ResidentMutexName), out failure);

    public static ResidentInstance? TryAcquire(AppIdentity identity) =>
        TryAcquire(identity, out _);

    // The factory is the seam the tests drive: the creation failures cannot be
    // provoked on a real mutex name.
    internal static ResidentInstance? TryAcquire(Func<Mutex> create, out string? failure)
    {
        failure = null;

        Mutex mutex;
        try { mutex = create(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or WaitHandleCannotBeOpenedException)
        {
            failure = ex.GetType().Name;
            return null;
        }

        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
            return new ResidentInstance(mutex, abandoned: false);
        }
        catch (AbandonedMutexException)
        {
            // The previous process died without orderly teardown. Windows grants
            // this thread ownership; recovery is safe because no old hook remains.
            return new ResidentInstance(mutex, abandoned: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            mutex.Dispose();
            failure = ex.GetType().Name;
            return null;
        }
    }
    // Returns the ApplicationException when this thread did not own the mutex
    // (a quit reached from another thread), so the caller can log it; the
    // handle closes either way. Null when released cleanly or already released.
    public ApplicationException? Release()
    {
        Mutex? mutex = _mutex;
        _mutex = null;
        if (mutex is null) return null;

        try
        {
            mutex.ReleaseMutex();
            return null;
        }
        catch (ApplicationException ex)
        {
            return ex;
        }
        finally
        {
            mutex.Dispose();
        }
    }

    public void Dispose() => Release();
}
