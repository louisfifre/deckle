using System.Globalization;

namespace Deckle.Diagnostics;

// Persists one selected event stream to a fixed JSONL destination. Write only
// performs a bounded in-memory hand-off; one dedicated worker serializes and
// appends entries in acceptance order. When the queue reaches its fixed cap,
// producers wait instead of losing rows or allowing memory to grow without
// bound. Flush is the explicit clean-shutdown boundary.
//
// The worker keeps the active stream open and reuses its JSON buffer. A normal
// line is still flushed to the OS before the next item, preserving the former
// process-crash posture without making the EventSource emitter perform disk I/O.
public sealed class JsonlSink : IFlushableLogSink
{
    private const int DefaultQueueCapacity = 1024;
    private static readonly byte[] NewLine = [(byte)'\n'];

    private readonly string _filePath;
    private readonly Func<EventEntry, bool> _predicate;
    private readonly string _kindLabel;
    private readonly JsonlSchema _schema;
    private readonly JsonlRotationPolicy? _rotation;
    private readonly JsonlLineSerializer _serializer = new();
    private readonly BoundedWriteQueue<EventEntry> _queue;

    // Worker-thread-owned state.
    private FileStream? _stream;
    private long _linesWritten;

    public JsonlSink(
        string filePath,
        string kindLabel,
        Func<EventEntry, bool> predicate,
        JsonlSchema schema = JsonlSchema.PayloadOnly,
        JsonlRotationPolicy? rotation = null,
        int queueCapacity = DefaultQueueCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(kindLabel);

        _filePath = filePath;
        _kindLabel = kindLabel;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        _schema = schema;
        _rotation = rotation;

        string? parent = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        if (_rotation is not null)
            _linesWritten = CountLines(_filePath);

        _queue = new BoundedWriteQueue<EventEntry>(
            queueCapacity,
            write: WriteOnWorker,
            flush: FlushOnWorker,
            close: CloseOnWorker);
    }

    public bool Wants(EventEntry entry) => _predicate(entry);

    public void Write(EventEntry entry) => _queue.Enqueue(entry);

    public void Flush() => _queue.Flush();

    public void Dispose() => _queue.Dispose();

    private async ValueTask WriteOnWorker(EventEntry entry)
    {
        try
        {
            if (_rotation is not null && _linesWritten >= _rotation.MaxLines)
                RollFiles();

            FileStream stream = EnsureStream();
            ReadOnlyMemory<byte> json = _serializer.Serialize(entry, _kindLabel, _schema);
            await stream.WriteAsync(json).ConfigureAwait(false);
            await stream.WriteAsync(NewLine).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            _linesWritten++;
        }
        catch
        {
            CloseOnWorker();
            throw;
        }
    }

    private FileStream EnsureStream()
    {
        return _stream ??= new FileStream(
            _filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private ValueTask FlushOnWorker() => _stream is null
        ? ValueTask.CompletedTask
        : new ValueTask(_stream.FlushAsync());

    private void CloseOnWorker()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private void RollFiles()
    {
        JsonlRotationPolicy? rotation = _rotation;
        if (rotation is null) return;

        CloseOnWorker();
        try
        {
            string directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
            string archiveDirectory = Path.Combine(directory, "archive");
            Directory.CreateDirectory(archiveDirectory);

            string fileName = Path.GetFileName(_filePath);
            int next = NextGeneration(archiveDirectory, fileName);
            string target = Path.Combine(archiveDirectory, $"{fileName}.{next:D4}");

            if (File.Exists(_filePath))
                File.Move(_filePath, target, overwrite: true);

            _linesWritten = 0;
        }
        catch
        {
            _linesWritten = CountLines(_filePath);
        }
    }

    private static int NextGeneration(string archiveDirectory, string fileName)
    {
        int max = 0;
        string prefix = fileName + ".";
        try
        {
            foreach (string path in Directory.EnumerateFiles(archiveDirectory, prefix + "*"))
            {
                string suffix = Path.GetFileName(path)[prefix.Length..];
                if (int.TryParse(
                        suffix,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int generation)
                    && generation > max)
                {
                    max = generation;
                }
            }
        }
        catch { }

        return max + 1;
    }

    private static long CountLines(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;

            long count = 0;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    if (buffer[i] == (byte)'\n') count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
