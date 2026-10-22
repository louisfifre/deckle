using System.Globalization;

namespace Deckle.Diagnostics;

// Persists one selected event stream to a fixed JSONL destination. Write only
// performs a bounded in-memory hand-off; one worker serializes and appends
// entries in acceptance order. When the queue reaches its fixed cap,
// producers wait instead of losing rows or allowing memory to grow without
// bound. Flush is the explicit clean-shutdown boundary.
//
// The worker keeps the active stream open. Each line is flushed to the OS
// before the next item, so a process crash keeps every written line, and the
// EventSource emitter never performs disk I/O.
//
// A destination that stops accepting writes (disk full, file locked, folder
// gone) is reported once as a sink incident on Deckle-Diagnostics, named by
// its file, and closed by the next line that lands; the other sinks carry the
// report. The queue's own comment says why those reports leave the worker.
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
        {
            // An archive left oversized by an earlier run (or by a cap lowered
            // since) is trimmed at start, not only at the next roll.
            PruneArchives(onWorker: false);
            _linesWritten = CountLines(_filePath);
        }

        string sinkName = Path.GetFileName(_filePath);
        _queue = new BoundedWriteQueue<EventEntry>(
            queueCapacity,
            write: WriteOnWorker,
            flush: FlushOnWorker,
            close: CloseOnWorker,
            onFault: ex => DeckleDiagnosticsSource.Log.SinkFaulted(
                sinkName, ex.GetType().Name, ex.Message),
            onRecovered: failures => DeckleDiagnosticsSource.Log.SinkRecovered(
                sinkName, failures));
    }

    public bool Wants(EventEntry entry) => _predicate(entry);

    public void Write(EventEntry entry) => _queue.Enqueue(entry);

    public void Flush() => _queue.Flush();

    public bool TryFlush(TimeSpan timeout) => _queue.TryFlush(timeout);

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
            string archiveDirectory = ArchiveDirectory();
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
            return;
        }

        // Only after a roll that worked: a failed move must not delete history.
        PruneArchives(onWorker: true);
    }

    private string ArchiveDirectory() =>
        Path.Combine(Path.GetDirectoryName(_filePath) ?? string.Empty, "archive");

    // Deletes the oldest generations beyond MaxArchives. Never throws: a log
    // that cannot trim itself must still keep writing, so a failure leaves the
    // remaining files alone.
    private void PruneArchives(bool onWorker)
    {
        int? cap = _rotation?.MaxArchives;
        if (cap is null) return;

        try
        {
            string archiveDirectory = ArchiveDirectory();
            if (!Directory.Exists(archiveDirectory)) return;

            string fileName = Path.GetFileName(_filePath);
            List<(int Number, string Path)> generations =
                [.. EnumerateGenerations(archiveDirectory, fileName)];

            // The numeric suffix is the age: lowest is oldest. File dates move
            // when a file is copied or restored, so they are never consulted.
            generations.Sort(static (a, b) => a.Number.CompareTo(b.Number));

            int excess = generations.Count - cap.Value;
            if (excess <= 0) return;

            int pruned = 0;
            for (int i = 0; i < excess; i++)
            {
                try
                {
                    File.Delete(generations[i].Path);
                    pruned++;
                }
                catch
                {
                    // Stop at the first failure so the survivors stay the
                    // newest contiguous run; the next roll tries again.
                    break;
                }
            }

            if (pruned == 0) return;

            int kept = generations.Count - pruned;
            if (onWorker)
            {
                // Emitting on the worker would dispatch back into this sink's
                // own bounded queue; with the queue full the worker would wait
                // on itself. A pool thread breaks that cycle.
                _ = Task.Run(() =>
                {
                    try { DeckleDiagnosticsSource.Log.ArchivesPruned(pruned, fileName, kept); }
                    catch { }
                });
            }
            else
            {
                DeckleDiagnosticsSource.Log.ArchivesPruned(pruned, fileName, kept);
            }
        }
        catch { }
    }

    private static int NextGeneration(string archiveDirectory, string fileName)
    {
        int max = 0;
        try
        {
            foreach ((int number, _) in EnumerateGenerations(archiveDirectory, fileName))
            {
                if (number > max) max = number;
            }
        }
        catch { }

        return max + 1;
    }

    // Archive files named `<fileName>.<digits>`; anything else in the folder is
    // not ours and is neither counted nor deleted.
    private static IEnumerable<(int Number, string Path)> EnumerateGenerations(
        string archiveDirectory,
        string fileName)
    {
        string prefix = fileName + ".";
        foreach (string path in Directory.EnumerateFiles(archiveDirectory, prefix + "*"))
        {
            string suffix = System.IO.Path.GetFileName(path)[prefix.Length..];
            if (int.TryParse(
                    suffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int generation))
            {
                yield return (generation, path);
            }
        }
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
