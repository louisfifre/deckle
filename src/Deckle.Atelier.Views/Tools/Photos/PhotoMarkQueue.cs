namespace Deckle.Atelier.Views;

// Runs the work on a folder's mark store off the UI thread, one piece after
// the other, in the order it was asked for.
//
// Why: PhotoMarkStore writes its file on every mark, and a sort is a key press
// per photo. The gallery changes the tile at once and hands the write here,
// so a key never waits on the disk. The store is not made for two threads, so
// every use of it, the reads included, goes through this one line of work.
// Chained tasks keep the order strictly; a semaphore's waiters do not promise it.
internal sealed class PhotoMarkQueue
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public Task Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    public Task<T> Run<T>(Func<T> work)
    {
        lock (_gate)
        {
            Task<T> next = _tail.ContinueWith(_ => work(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            _tail = next;
            return next;
        }
    }
}
