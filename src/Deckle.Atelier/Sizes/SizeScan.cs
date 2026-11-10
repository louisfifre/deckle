using System.Diagnostics;
using System.IO.Enumeration;
using System.Threading.Channels;

namespace Deckle.Atelier;

// One analysis of a place: the size of every folder beneath it and its heaviest
// files. It starts at once on the thread pool; the view polls Progress and
// Snapshot() while it runs, awaits Completion for the final report, and stops
// it with Cancel. Running the analysis again (F5, 12.21) is a new scan: a scan
// never restarts, so a report in hand never changes under the view.
//
// How it reads. A queue of folders and a few workers: each worker takes a
// folder, reads its entries once (FolderReader), queues its sub-folders and
// goes on. A folder completes when its last sub-folder does, which adds its
// total to its parent (SizeNode), so no worker ever waits on another. Workers
// = 1 reads the same way in one thread.
//
// Errors. A folder that cannot be read is counted and the scan goes on; only a
// place that cannot be opened at all, or an exception nobody expected, fails
// the scan. Cancelling keeps what was measured.
public sealed class SizeScan
{
    private static long s_lastId;

    private readonly SizeScanOptions _options;
    private readonly EnumerationOptions _enumeration;
    private readonly ScanCounters _counters = new();
    private readonly CancellationTokenSource _cancel;
    private readonly Channel<SizeNode> _queue = Channel.CreateUnbounded<SizeNode>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Stopwatch _clock = new();
    private readonly TaskCompletionSource _rootDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _id = Interlocked.Increment(ref s_lastId);
    private SizeNode? _root;
    private int _state = (int)SizeScanState.Running;
    private FolderListingFailure _placeFailure;
    private DateTimeOffset? _finishedAt;

    private SizeScan(string place, SizeScanOptions options, CancellationToken cancel)
    {
        Place = place;
        _options = options;
        _enumeration = FolderReader.OptionsFor(options.BufferSize);
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
    }

    public string Place { get; }

    public SizeScanState State => (SizeScanState)Volatile.Read(ref _state);

    public SizeProgress Progress => _counters.Snapshot(State, _clock.Elapsed);

    public Task<SizeReport> Completion { get; private set; } = null!;

    public static SizeScan Start(string place, SizeScanOptions? options = null, CancellationToken cancel = default)
    {
        options ??= new SizeScanOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Workers, 1);
        var scan = new SizeScan(FolderPaths.Normalize(place), options, cancel);
        scan.Completion = Task.Run(scan.RunAsync);
        return scan;
    }

    public void Cancel()
    {
        try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
    }

    // What is measured so far, for the list while the scan runs (12.23): a
    // sub-folder not measured yet has no size.
    public SizeReport Snapshot() =>
        new(Place, _root, State, _placeFailure, _finishedAt, _clock.Elapsed);

    private async Task<SizeReport> RunAsync()
    {
        _clock.Start();
        DeckleAtelierSource.Log.SizeScanStarted(_id, _options.Workers);
        SizeScanState outcome;
        try
        {
            outcome = await ScanAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An exception the design did not plan for: the view shows the
            // analysis as failed, the log says what threw.
            DeckleAtelierSource.Log.SizeScanFailed(_id, ex.GetType().Name);
            outcome = SizeScanState.Failed;
        }
        finally
        {
            _queue.Writer.TryComplete();
            _cancel.Dispose();
        }

        _clock.Stop();
        _finishedAt = DateTimeOffset.Now;
        Volatile.Write(ref _state, (int)outcome);

        SizeProgress done = Progress;
        if (done.UnreadableFolders > 0)
            DeckleAtelierSource.Log.SizeScanPartial(_id, done.UnreadableFolders);
        DeckleAtelierSource.Log.SizeScanEnded(_id, outcome.ToString(), done.FoldersSeen, done.FilesSeen,
            done.UnreadableFolders, done.LinksSkipped, (long)done.Elapsed.TotalMilliseconds);
        return Snapshot();
    }

    private async Task<SizeScanState> ScanAsync()
    {
        _placeFailure = PlaceFailure(Place);
        if (_placeFailure != FolderListingFailure.None) return SizeScanState.Failed;

        _root = new SizeNode(FolderPaths.DisplayName(Place), Place, parent: null,
            keepsChildren: true, _options.HeaviestCount);
        _queue.Writer.TryWrite(_root);

        CancellationToken cancel = _cancel.Token;
        var workers = new Task[_options.Workers];
        for (int i = 0; i < workers.Length; i++)
            workers[i] = Task.Run(() => WorkAsync(cancel), CancellationToken.None);

        try
        {
            await _rootDone.Task.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }

        _queue.Writer.TryComplete();
        await Task.WhenAll(workers).ConfigureAwait(false);
        return _root.IsComplete ? SizeScanState.Completed : SizeScanState.Cancelled;
    }

    private async Task WorkAsync(CancellationToken cancel)
    {
        var subfolders = new List<string>();
        try
        {
            await foreach (SizeNode node in _queue.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
            {
                Measure(node, subfolders, cancel);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Fail the whole scan rather than leave a folder that never
            // completes and a view waiting on it.
            _rootDone.TrySetException(ex);
        }
    }

    private void Measure(SizeNode node, List<string> subfolders, CancellationToken cancel)
    {
        subfolders.Clear();
        bool read = FolderReader.Read(node, _enumeration, _counters, cancel, subfolders,
            out long looseBytes, out int looseFiles);
        _counters.FolderRead(!read);

        bool childrenKept = node.Depth + 1 <= _options.NavigableDepth;
        var children = new List<SizeNode>(subfolders.Count);
        foreach (string name in subfolders)
        {
            children.Add(new SizeNode(name, Path.Join(node.Path, name), node, childrenKept, _options.HeaviestCount));
        }
        node.Read(looseBytes, looseFiles, unreadable: !read, children);
        if (node.Parent is null) _counters.ChildrenKnown(children.Count);

        foreach (SizeNode child in children) _queue.Writer.TryWrite(child);
        Release(node);
    }

    // Climbs from a node as far as each release completes a folder.
    private void Release(SizeNode node)
    {
        SizeNode? current = node;
        while (current is not null && current.Release())
        {
            current.Complete();
            SizeNode? parent = current.Parent;
            if (parent is null)
            {
                _rootDone.TrySetResult();
                return;
            }
            parent.Absorb(current);
            if (parent.Parent is null) _counters.ChildMeasured();
            current = parent;
        }
    }

    private static FolderListingFailure PlaceFailure(string place)
    {
        try
        {
            if (!Directory.Exists(place)) return FolderListingFailure.NotFound;
            using IEnumerator<string> probe = Directory.EnumerateFileSystemEntries(place).GetEnumerator();
            probe.MoveNext();
            return FolderListingFailure.None;
        }
        catch (UnauthorizedAccessException) { return FolderListingFailure.AccessDenied; }
        catch (IOException) { return FolderListingFailure.Unreadable; }
    }
}
