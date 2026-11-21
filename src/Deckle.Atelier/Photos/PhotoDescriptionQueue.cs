namespace Deckle.Atelier;

// The one line every description waits in: a photo asked alone ("Describe
// this photo", "Describe again") and the photos of a folder run
// ("Describe the folder") go through the same model one at a time, so both can
// be asked at once (spec, Open question 27, drawn: the folder command stays
// usable while one photo is being described) without two pictures loading a
// local model's memory side by side.
//
// A photo asked alone goes before the run's next photo: the person is
// waiting on that one, while a run of four hundred can take one more step
// later. Runs of several folders take turns in the order they were started.
//
// Every description written is saved in its folder's store before the
// caller hears of it, so a view that reads the store after the answer, or on
// the store's Changed event, sees it.
public sealed class PhotoDescriptionQueue : IDisposable
{
    private readonly IPhotoDescriber _describer;
    private readonly object _lock = new();
    private readonly Queue<SingleRequest> _singles = new();
    private readonly List<PhotoDescriptionRun> _runs = [];
    private readonly CancellationTokenSource _disposed = new();
    private bool _working;
    private long _nextId;

    public PhotoDescriptionQueue(IPhotoDescriber describer) => _describer = describer;

    // The runs still going, in the order they were started.
    public IReadOnlyList<PhotoDescriptionRun> Runs
    {
        get { lock (_lock) return [.. _runs]; }
    }

    // The run going on in this folder, if any.
    public PhotoDescriptionRun? RunOf(string folder)
    {
        string normalized = FolderPaths.Normalize(folder);
        lock (_lock) return _runs.FirstOrDefault(r => string.Equals(r.Folder, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Describes one photo of <paramref name="store"/>'s folder,
    /// even one that has a description already ("Describe again").
    /// Cancelling <paramref name="cancellationToken"/> stops it, waiting or
    /// under way, and answers Stopped.</summary>
    public Task<PhotoDescriptionResult> DescribeOneAsync(PhotoDescriptionStore store, string fileName, CancellationToken cancellationToken)
    {
        var request = new SingleRequest(Interlocked.Increment(ref _nextId), store, fileName, cancellationToken);
        DeckleAtelierSource.Log.PhotoDescriptionStarted(request.Id);
        lock (_lock)
        {
            if (_disposed.IsCancellationRequested) return Task.FromResult(PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped));
            _singles.Enqueue(request);
            StartWorkerIfIdle();
        }
        return request.Done.Task;
    }

    /// <summary>Goes through <paramref name="photos"/> of
    /// <paramref name="store"/>'s folder and describes each one that has no
    /// description. A folder that has a run going gets that run back.</summary>
    public PhotoDescriptionRun StartFolder(PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos)
    {
        lock (_lock)
        {
            if (RunOfLocked(store.Folder) is { } going) return going;

            var run = new PhotoDescriptionRun(Interlocked.Increment(ref _nextId), store, [.. photos]);
            if (_disposed.IsCancellationRequested)
            {
                run.End(PhotoDescriptionRunState.Stopped);
                return run;
            }
            DeckleAtelierSource.Log.PhotoDescriptionRunStarted(run.Id, run.Total, run.Described);
            _runs.Add(run);
            StartWorkerIfIdle();
            return run;
        }
    }

    private PhotoDescriptionRun? RunOfLocked(string normalizedFolder) =>
        _runs.FirstOrDefault(r => string.Equals(r.Folder, normalizedFolder, StringComparison.OrdinalIgnoreCase));

    // Under the lock.
    private void StartWorkerIfIdle()
    {
        if (_working) return;
        _working = true;
        _ = Task.Run(WorkAsync);
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            Func<Task>? next;
            var ended = new List<PhotoDescriptionRun>();
            lock (_lock)
            {
                next = TakeNextLocked(ended);
                if (next is null) _working = false;
            }
            foreach (PhotoDescriptionRun run in ended) Finish(run);
            if (next is null) return;
            await next();
        }
    }

    // The next piece of work, or null when there is none. Runs found with
    // nothing left are ended and handed back through `ended`, so their
    // events are raised outside the lock.
    private Func<Task>? TakeNextLocked(List<PhotoDescriptionRun> ended)
    {
        while (_singles.TryDequeue(out SingleRequest? request))
        {
            if (!request.Cancellation.IsCancellationRequested && !_disposed.IsCancellationRequested)
                return () => DescribeSingleAsync(request);
            EndSingle(request, PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped), 0);
        }

        while (_runs.Count > 0)
        {
            PhotoDescriptionRun run = _runs[0];
            if (run.StopToken.IsCancellationRequested || _disposed.IsCancellationRequested)
            {
                EndRunLocked(run, PhotoDescriptionRunState.Stopped, null, ended);
                continue;
            }
            if (run.TakeNext() is { } photo)
            {
                // The run steps back to the end of the line, so another
                // folder's run takes the next turn.
                _runs.RemoveAt(0);
                _runs.Add(run);
                return () => DescribeRunPhotoAsync(run, photo);
            }
            EndRunLocked(run, PhotoDescriptionRunState.Completed, null, ended);
        }
        return null;
    }

    private async Task DescribeSingleAsync(SingleRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Cancellation, _disposed.Token);
        PhotoDescriptionResult result = await DescribeSafelyAsync(Path.Combine(request.Store.Folder, request.FileName), linked.Token);
        if (result.Description is { } description) request.Store.Set(request.FileName, description);
        EndSingle(request, result, sw.ElapsedMilliseconds);
    }

    private static void EndSingle(SingleRequest request, PhotoDescriptionResult result, long elapsedMs)
    {
        string outcome = result.Succeeded ? "Completed" : result.Failure == PhotoDescriptionFailure.Stopped ? "Stopped" : "Failed";
        if (result.Failure is { } failure && failure != PhotoDescriptionFailure.Stopped)
            DeckleAtelierSource.Log.PhotoDescriptionFailed(request.Id, failure.ToString());
        DeckleAtelierSource.Log.PhotoDescriptionEnded(request.Id, outcome, elapsedMs);
        request.Done.TrySetResult(result);
    }

    private async Task DescribeRunPhotoAsync(PhotoDescriptionRun run, PhotoFile photo)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.StopToken, _disposed.Token);
        PhotoDescriptionResult result = await DescribeSafelyAsync(photo.Path, linked.Token);

        // Saved outside the lock: the store raises Changed on this thread.
        if (result.Description is { } description) run.Store.Set(photo.Name, description);

        var ended = new List<PhotoDescriptionRun>();
        lock (_lock)
        {
            if (result.Failure == PhotoDescriptionFailure.Stopped)
                EndRunLocked(run, PhotoDescriptionRunState.Stopped, null, ended);
            else if (result.Failure is { } failure && PhotoDescriptionResult.IsSetupFailure(failure))
                EndRunLocked(run, PhotoDescriptionRunState.Failed, failure, ended);
            else if (result.Failure is not null)
                run.CountFailed();
        }
        DeckleAtelierSource.Log.PhotoDescribedDetail(run.Id, result.Failure?.ToString() ?? "Described", sw.ElapsedMilliseconds);
        if (ended.Count > 0) Finish(run);
        else run.RaiseChanged();
    }

    // A describer that throws is a fault of its own, not of the photo: it
    // answers Other, so one bad call never stops the line.
    private async Task<PhotoDescriptionResult> DescribeSafelyAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _describer.DescribeAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Stopped);
        }
        catch (Exception ex)
        {
            DeckleAtelierSource.Log.PhotoDescriberFaulted(ex.GetType().Name);
            return PhotoDescriptionResult.Failed(PhotoDescriptionFailure.Other);
        }
    }

    private void EndRunLocked(PhotoDescriptionRun run, PhotoDescriptionRunState state, PhotoDescriptionFailure? failure, List<PhotoDescriptionRun> ended)
    {
        if (!run.IsRunning) return;
        run.End(state, failure);
        _runs.Remove(run);
        ended.Add(run);
    }

    // Outside the lock: the end's events, then the run's own.
    private static void Finish(PhotoDescriptionRun run)
    {
        if (run.SetupFailure is { } failure)
            DeckleAtelierSource.Log.PhotoDescriptionRunAbandoned(run.Id, failure.ToString(), run.Failed);
        else if (run.Failed > 0)
            DeckleAtelierSource.Log.PhotoDescriptionRunPartial(run.Id, run.Failed);
        DeckleAtelierSource.Log.PhotoDescriptionRunEnded(
            run.Id, run.State.ToString(), run.Described, run.Total, run.Failed, run.Skipped, run.ElapsedMs);
        run.RaiseChanged();
    }

    // Stops what is under way and what waits; a run still listed ends as
    // stopped at the worker's next turn.
    public void Dispose()
    {
        try { _disposed.Cancel(); } catch (ObjectDisposedException) { }
        lock (_lock)
        {
            StartWorkerIfIdle();
        }
    }

    private sealed record SingleRequest(long Id, PhotoDescriptionStore Store, string FileName, CancellationToken Cancellation)
    {
        public TaskCompletionSource<PhotoDescriptionResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
