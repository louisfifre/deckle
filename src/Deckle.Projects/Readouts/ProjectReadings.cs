using System.Diagnostics;

namespace Deckle.Projects;

// The cache the window reads: the last reading of every project, refreshed on
// demand.
//
// Get gives the previous reading, or null before the first one: the cards show
// the previous values while a refresh runs, and nothing on a first read (spec,
// Readouts). RefreshAsync reads at most three projects at a time (each is a
// handful of git processes, about 50 to 150 ms), and raises ReadingChanged for
// each project as it finishes, on the thread that finished it; the view
// marshals to its own thread.
//
// The window refreshes when a screen opens, when it comes back to the front,
// on F5 and when an action ends. Two refreshes of one project at once (F5
// pressed twice) are one read: the second awaits the first.
//
// A read is shared, so it belongs to no caller: it runs on the cache's own
// token, and a caller leaving (its token cancelled) only stops waiting for it.
// Otherwise the page that left first would cancel the read the page that stayed
// is waiting on, and nothing would be stored. Dispose cancels what still runs.
public sealed class ProjectReadings : IDisposable
{
    public const int MaxParallelReads = 3;

    private readonly IProjectReader _reader;
    private readonly SemaphoreSlim _slots = new(MaxParallelReads);
    private readonly Dictionary<string, ProjectReading> _readings = [];
    private readonly Dictionary<string, Task> _inFlight = [];
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();

    public ProjectReadings(IProjectReader reader) => _reader = reader;

    // Raised with the project's id when its reading changed. Not raised for a
    // read that was cancelled.
    public event Action<string>? ReadingChanged;

    public ProjectReading? Get(string projectId)
    {
        lock (_gate) return _readings.GetValueOrDefault(projectId);
    }

    // Forgets a project that left the store, so its reading cannot come back.
    public void Forget(string projectId)
    {
        lock (_gate) _readings.Remove(projectId);
    }

    public async Task RefreshAsync(IEnumerable<ProjectEntry> projects, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        List<Task> reads = [.. projects.Select(ReadOnceAsync)];
        if (reads.Count == 0) return;

        try
        {
            await Task.WhenAll(reads).WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The person left the screen: the reads go on for whoever shares
            // them, the previous readings stand, and this caller stops waiting.
            return;
        }
        DeckleProjectsSource.Log.ProjectsRead(reads.Count, clock.ElapsedMilliseconds);
    }

    // Cancels the reads still running; the cache is not used after that.
    public void Dispose() => _lifetime.Cancel();

    private Task ReadOnceAsync(ProjectEntry project)
    {
        lock (_gate)
        {
            if (_inFlight.TryGetValue(project.Id, out Task? running)) return running;
            // On the pool, so the read cannot finish (and clear its entry)
            // before the entry is made.
            Task task = Task.Run(() => ReadAsync(project, _lifetime.Token), CancellationToken.None);
            _inFlight[project.Id] = task;
            return task;
        }
    }

    private async Task ReadAsync(ProjectEntry project, CancellationToken cancellation)
    {
        try
        {
            await _slots.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                ProjectReading reading = await _reader.ReadAsync(project.Path, cancellation).ConfigureAwait(false);
                lock (_gate) _readings[project.Id] = reading;
            }
            finally
            {
                _slots.Release();
            }
            ReadingChanged?.Invoke(project.Id);
        }
        catch (OperationCanceledException)
        {
            // The cache was disposed: the previous reading stands.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // One project that cannot be read leaves the others to finish, and
            // its previous reading stands.
            DeckleProjectsSource.Log.ReadingFailed(ex.GetType().Name);
        }
        finally
        {
            lock (_gate) _inFlight.Remove(project.Id);
        }
    }
}
