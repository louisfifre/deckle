namespace Deckle.App;

// The resident-instance gate: one named mutex, taken at startup before any sink
// or hook exists. Kept apart from App so the failure paths can be exercised
// with a mutex factory that throws.
internal static class ResidentMutexGate
{
    internal enum Outcome
    {
        Acquired,
        AcquiredFromAbandoned,
        HeldByAnother,
        // Creating or waiting on the mutex failed (access denied, I/O error,
        // name taken by another kind of object). Treated like a second launch:
        // closing is safe, while continuing could run two keyboard hooks.
        CreationFailed,
    }

    internal static Outcome TryAcquire(Func<Mutex> create, out Mutex? mutex, out string? failure)
    {
        mutex = null;
        failure = null;

        Mutex created;
        try { created = create(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or IOException
                                       or WaitHandleCannotBeOpenedException)
        {
            failure = ex.GetType().Name;
            return Outcome.CreationFailed;
        }

        bool abandoned = false;
        bool acquired;
        try
        {
            acquired = created.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // The previous process died without orderly teardown. Windows grants
            // this thread ownership; recovery is safe because no old hook remains.
            acquired = true;
            abandoned = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            created.Dispose();
            failure = ex.GetType().Name;
            return Outcome.CreationFailed;
        }

        if (!acquired)
        {
            created.Dispose();
            return Outcome.HeldByAnother;
        }

        mutex = created;
        return abandoned ? Outcome.AcquiredFromAbandoned : Outcome.Acquired;
    }
}
