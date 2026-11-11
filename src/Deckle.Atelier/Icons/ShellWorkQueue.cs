using System.Collections.Concurrent;

namespace Deckle.Atelier;

// One thread of its own, in a single-threaded apartment, that runs shell
// calls in the order they come.
//
// Why a thread of its own: IShellItemImageFactory::GetImage "generally should
// not be called from a UI thread ... An extraction should never be done on a
// UI thread" (Microsoft Learn). Why a single-threaded apartment and not the
// thread pool: pool threads are in the multithreaded apartment, while the
// shell asks for COM initialised with CoInitialize or OleInitialize (SHGetFileInfo,
// Microsoft Learn), both apartment-threaded; the icon handlers and thumbnail
// providers it loads are, for most of them, apartment-threaded objects that
// would otherwise be created in a hidden host apartment and reached through
// marshalling. A .NET thread set to STA before it starts is initialised so,
// and its waits pump COM messages, as an STA thread must.
//
// A job whose token is cancelled before its turn is skipped, so a folder left
// with five hundred rows still waiting costs five hundred skips, not five
// hundred reads. A job already running cannot be stopped: GetImage takes no
// token. Its caller stops waiting at once all the same.
public sealed class ShellWorkQueue : IDisposable
{
    private readonly BlockingCollection<Action> _jobs = [];
    private readonly Thread _thread;

    public ShellWorkQueue(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return Task.FromCanceled<T>(cancellation);

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _jobs.Add(() =>
            {
                if (cancellation.IsCancellationRequested || _jobs.IsAddingCompleted)
                {
                    done.TrySetCanceled(cancellation.IsCancellationRequested ? cancellation : new CancellationToken(canceled: true));
                    return;
                }
                try { done.TrySetResult(work()); }
                catch (Exception ex) { done.TrySetException(ex); }
            });
        }
        catch (InvalidOperationException)
        {
            // Disposed: the window is closing and nobody waits for the answer.
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        }
        return done.Task.WaitAsync(cancellation);
    }

    private void Run()
    {
        foreach (Action job in _jobs.GetConsumingEnumerable()) job();
    }

    // Lets the thread finish the job in hand and end; the jobs still queued
    // are dropped.
    public void Dispose() => _jobs.CompleteAdding();
}
