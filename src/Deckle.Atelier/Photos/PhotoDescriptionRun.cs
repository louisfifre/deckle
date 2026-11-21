namespace Deckle.Atelier;

public enum PhotoDescriptionRunState
{
    Running,
    // Every photo was gone through; some may have failed (Failed > 0).
    Completed,
    // The person stopped it.
    Stopped,
    // A failure that holds for every photo (no model chosen, server not
    // reachable, the model cannot read images, endpoint not local) ended it:
    // SetupFailure says which.
    Failed,
}

// One folder gone through photo after photo, for "Describe the folder" (12.4):
// "126 of 412 described", a progress bar and a way to stop (12.19).
//
// Described counts the photos of the folder that have a description, the
// ones described before the run included, so the counter reads as the
// boards draw it; Total is the folder's photos. A photo that already has a
// description is skipped at its turn, not at the start, so one described
// alone meanwhile is not described twice. A photo that fails is counted in
// Failed and the run goes on.
//
// Changed is raised on the queue's thread after each photo and at the end; a
// view hops to its own thread.
public sealed class PhotoDescriptionRun
{
    private readonly IReadOnlyList<PhotoFile> _photos;
    private readonly Queue<PhotoFile> _pending;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();

    internal PhotoDescriptionRun(long id, PhotoDescriptionStore store, IReadOnlyList<PhotoFile> photos)
    {
        Id = id;
        Store = store;
        _photos = photos;
        _pending = new Queue<PhotoFile>(photos);
    }

    internal long Id { get; }

    public PhotoDescriptionStore Store { get; }

    public string Folder => Store.Folder;

    public int Total => _photos.Count;

    // Read from the store each time, so a photo described alone during the
    // run counts too.
    public int Described => Store.CountDescribed(_photos);

    public int Failed { get; private set; }

    // Photos found already described at their turn.
    public int Skipped { get; private set; }

    public PhotoDescriptionRunState State { get; private set; } = PhotoDescriptionRunState.Running;

    public PhotoDescriptionFailure? SetupFailure { get; private set; }

    public bool IsRunning => State == PhotoDescriptionRunState.Running;

    // Completes when the run has ended, whatever the way.
    public Task Completion => _done.Task;

    public event Action<PhotoDescriptionRun>? Changed;

    internal CancellationToken StopToken => _stop.Token;

    internal long ElapsedMs => _elapsed.ElapsedMilliseconds;

    // Ends the run: the photo being described is stopped, the rest are left.
    public void Stop()
    {
        if (!IsRunning) return;
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
    }

    // The next photo still to describe, skipping those that have a
    // description by now; null when there is none left. Called under the
    // queue's lock.
    internal PhotoFile? TakeNext()
    {
        while (_pending.TryDequeue(out PhotoFile? photo))
        {
            if (!Store.IsDescribed(photo.Name)) return photo;
            Skipped++;
        }
        return null;
    }

    internal void CountFailed() => Failed++;

    internal void End(PhotoDescriptionRunState state, PhotoDescriptionFailure? setupFailure = null)
    {
        State = state;
        SetupFailure = setupFailure;
        _pending.Clear();
        _elapsed.Stop();
    }

    internal void RaiseChanged()
    {
        Changed?.Invoke(this);
        if (!IsRunning) _done.TrySetResult();
    }
}
