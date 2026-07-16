namespace Deckle.Diagnostics;

// Routes one dataset stream over a dynamic JSONL tree. A single bounded worker
// preserves acceptance order and owns every file handle; emitter threads never
// open, serialize, flush, or lock a destination file.
//
// Dynamic destinations are held in a fixed-size LRU. Eviction flushes and
// closes the least recently used stream before a new one opens, so an evolving
// corpus cannot turn path diversity into an unbounded handle cache.
public sealed class RoutedJsonlSink : IFlushableLogSink
{
    private const int DefaultQueueCapacity = 1024;
    private const int DefaultMaxOpenFiles = 16;
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly Func<EventEntry, string> _pathResolver;
    private readonly Func<EventEntry, bool> _predicate;
    private readonly string _kindLabel;
    private readonly int _maxOpenFiles;
    private readonly JsonlLineSerializer _serializer = new();
    private readonly BoundedWriteQueue<EventEntry> _queue;

    // Worker-thread-owned LRU state.
    private readonly Dictionary<string, OpenFile> _openFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _leastRecentlyUsed = new();

    public RoutedJsonlSink(
        Func<EventEntry, string> pathResolver,
        string kindLabel,
        Func<EventEntry, bool> predicate,
        int maxOpenFiles = DefaultMaxOpenFiles,
        int queueCapacity = DefaultQueueCapacity)
    {
        if (maxOpenFiles <= 0) throw new ArgumentOutOfRangeException(nameof(maxOpenFiles));
        ArgumentException.ThrowIfNullOrWhiteSpace(kindLabel);

        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _kindLabel = kindLabel;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _maxOpenFiles = maxOpenFiles;

        _queue = new BoundedWriteQueue<EventEntry>(
            queueCapacity,
            write: WriteOnWorker,
            flush: FlushOnWorker,
            close: CloseOnWorker);
    }

    internal int OpenFileCount => _openFiles.Count;

    public bool Wants(EventEntry entry) => _predicate(entry);

    public void Write(EventEntry entry) => _queue.Enqueue(entry);

    public void Flush() => _queue.Flush();

    public void Dispose() => _queue.Dispose();

    private async ValueTask WriteOnWorker(EventEntry entry)
    {
        string path = _pathResolver(entry);
        if (string.IsNullOrWhiteSpace(path)) return;

        OpenFile destination = GetOrOpen(path);
        try
        {
            ReadOnlyMemory<byte> json = _serializer.Serialize(
                entry,
                _kindLabel,
                JsonlSchema.PayloadOnly);
            await destination.Stream.WriteAsync(json).ConfigureAwait(false);
            await destination.Stream.WriteAsync(NewLine).ConfigureAwait(false);
            await destination.Stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            RemoveAndClose(path, destination);
            throw;
        }
    }

    private OpenFile GetOrOpen(string path)
    {
        if (_openFiles.TryGetValue(path, out OpenFile? existing))
        {
            _leastRecentlyUsed.Remove(existing.Node);
            _leastRecentlyUsed.AddFirst(existing.Node);
            return existing;
        }

        if (_openFiles.Count >= _maxOpenFiles)
            EvictLeastRecentlyUsed();

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var node = new LinkedListNode<string>(path);
        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var opened = new OpenFile(stream, node);
        _leastRecentlyUsed.AddFirst(node);
        _openFiles.Add(path, opened);
        return opened;
    }

    private void EvictLeastRecentlyUsed()
    {
        LinkedListNode<string>? node = _leastRecentlyUsed.Last;
        if (node is null) return;

        if (_openFiles.Remove(node.Value, out OpenFile? opened))
        {
            opened.Stream.Flush();
            opened.Stream.Dispose();
        }
        _leastRecentlyUsed.Remove(node);
    }

    private void RemoveAndClose(string path, OpenFile opened)
    {
        _openFiles.Remove(path);
        _leastRecentlyUsed.Remove(opened.Node);
        opened.Stream.Dispose();
    }

    private async ValueTask FlushOnWorker()
    {
        foreach (OpenFile opened in _openFiles.Values)
            await opened.Stream.FlushAsync().ConfigureAwait(false);
    }

    private void CloseOnWorker()
    {
        foreach (OpenFile opened in _openFiles.Values)
            opened.Stream.Dispose();
        _openFiles.Clear();
        _leastRecentlyUsed.Clear();
    }

    private sealed record OpenFile(FileStream Stream, LinkedListNode<string> Node);
}
