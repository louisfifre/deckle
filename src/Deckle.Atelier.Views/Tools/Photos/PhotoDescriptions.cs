using Deckle.Core;

namespace Deckle.Atelier.Views;

// What the photo tool and the file tool share of the descriptions: the one
// queue every description waits in (PhotoDescriptionQueue), one store per
// folder, the photos asked alone that are still under way, and the last
// failure of each, so a panel built again for a photo shows where it stands.
//
// It belongs to the app, not to a window (AtelierToolCatalog keeps one for
// the process): a folder run goes on when the person leaves the folder or
// closes the window, and the folder shows its progress again when it is
// opened. A store is kept per folder for the same reason: the run writes into
// the very store a panel reads, so a description written shows at once.
//
// PhotoChanged is raised on whatever thread the change happened on, the
// queue's most often; a view hops to its own thread.
internal sealed class PhotoDescriptions
{
    private readonly PhotoDescriptionQueue _queue;
    private readonly string _moduleDirectory;
    private readonly Func<PhotoDescriptionFailure?> _readiness;
    private readonly Func<Action?> _openSettings;
    private readonly object _lock = new();
    private readonly Dictionary<string, PhotoDescriptionStore> _stores = new(FolderPaths.Comparer);
    private readonly Dictionary<string, CancellationTokenSource> _asked = new(FolderPaths.Comparer);
    private readonly Dictionary<string, PhotoDescriptionFailure> _failures = new(FolderPaths.Comparer);

    public PhotoDescriptions(PhotoDescriptionQueue queue, string moduleDirectory,
                             Func<PhotoDescriptionFailure?> readiness, Func<Action?> openSettings)
    {
        _queue = queue;
        _moduleDirectory = moduleDirectory;
        _readiness = readiness;
        _openSettings = openSettings;
    }

    // The photo's full path whose state changed.
    public event Action<string>? PhotoChanged;

    // Opens Deckle's settings where the model is chosen; null when the app
    // has no such page (the rewriting module is not installed).
    public Action? OpenSettings => _openSettings();

    public int RunsGoingOn => _queue.Runs.Count;

    // What stops any description before one is asked, without asking a
    // server: no model chosen, a provider that is not on this machine. Read
    // off the UI thread: the choice is a small file.
    public Task<PhotoDescriptionFailure?> ReadinessAsync() => Task.Run(_readiness);

    public PhotoDescriptionStore StoreOf(string folder)
    {
        lock (_lock)
        {
            if (_stores.TryGetValue(folder, out PhotoDescriptionStore? store)) return store;
            store = new PhotoDescriptionStore(_moduleDirectory, folder);
            string storeFolder = store.Folder;
            store.Changed += name => PhotoChanged?.Invoke(Path.Combine(storeFolder, name));
            _stores[folder] = store;
            return store;
        }
    }

    public PhotoDescriptionState StateOf(PhotoFile photo)
    {
        PhotoDescriptionStore store = StoreOf(FolderOf(photo));
        lock (_lock)
        {
            if (_asked.ContainsKey(photo.Path)) return new PhotoDescriptionState(null, true, null);
            PhotoDescription? description = store.DescriptionOf(photo.Name);
            PhotoDescriptionFailure? failure = _failures.TryGetValue(photo.Path, out var f) ? f : null;
            return new PhotoDescriptionState(description, false, description is null ? failure : null);
        }
    }

    // "Describe this photo", "Describe again", "Try again". A photo
    // already under way is left as it is.
    public async Task DescribeAsync(PhotoFile photo)
    {
        PhotoDescriptionStore store = StoreOf(FolderOf(photo));
        var stop = new CancellationTokenSource();
        lock (_lock)
        {
            if (_asked.ContainsKey(photo.Path)) return;
            _asked[photo.Path] = stop;
            _failures.Remove(photo.Path);
        }
        PhotoChanged?.Invoke(photo.Path);

        PhotoDescriptionResult result;
        if (await ReadinessAsync() is { } notReady)
        {
            DeckleAtelierSource.Log.PhotoDescriptionNotStarted("photo", notReady.ToString());
            result = PhotoDescriptionResult.Failed(notReady);
        }
        else
        {
            result = await _queue.DescribeOneAsync(store, photo.Name, stop.Token);
        }

        lock (_lock)
        {
            _asked.Remove(photo.Path);
            if (result.Failure is { } failure) _failures[photo.Path] = failure;
        }
        stop.Dispose();
        PhotoChanged?.Invoke(photo.Path);
    }

    // "Stop" on a photo asked alone: the queue answers Stopped.
    public void Stop(PhotoFile photo)
    {
        lock (_lock)
        {
            if (_asked.TryGetValue(photo.Path, out CancellationTokenSource? stop))
            {
                try { stop.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public PhotoDescriptionRun? RunOf(string folder) => _queue.RunOf(folder);

    /// <summary>"Describe the folder". Null, with the reason, when the model
    /// cannot be used as set up; a folder that has a run going gets it back.</summary>
    public async Task<(PhotoDescriptionRun? Run, PhotoDescriptionFailure? NotReady)> StartFolderAsync(
        string folder, IReadOnlyList<PhotoFile> photos)
    {
        if (await ReadinessAsync() is { } notReady)
        {
            DeckleAtelierSource.Log.PhotoDescriptionNotStarted("folder", notReady.ToString());
            return (null, notReady);
        }
        return (_queue.StartFolder(StoreOf(folder), photos), null);
    }

    private static string FolderOf(PhotoFile photo) => Path.GetDirectoryName(photo.Path) ?? photo.Path;
}

// Where one photo's description stands, as its panel shows it: written
// (Description), under way (InProgress), failed or stopped (Failure), or none
// of these: no description yet.
internal sealed record PhotoDescriptionState(PhotoDescription? Description, bool InProgress, PhotoDescriptionFailure? Failure);
