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
//
// Rotation, pruning and line counting never stop the writes: a failure leaves
// the file growing or the archive unpruned. That is a second incident of the
// same shape (RotationFailed once, the attempts counted, RotationRecovered when
// a roll and its prune go through), so the journal says that a file stopped
// rolling instead of leaving a disk to fill up unexplained.
public sealed class JsonlSink : IFlushableLogSink
{
    private const int DefaultQueueCapacity = 1024;

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

    // Failed maintenance attempts of the open incident. Owned by the constructor
    // until the queue exists, by the worker after.
    private int _maintenanceFailures;

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
            _linesWritten = CountLines(_filePath, out Exception? countError);
            if (countError is not null)
                ReportMaintenanceFailure("count_lines", countError, onWorker: false);
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
            // One write for the row and its newline: two writes could be torn
            // between them, leaving an unterminated row.
            ReadOnlyMemory<byte> line = _serializer.Serialize(entry, _kindLabel, _schema);
            await stream.WriteAsync(line).ConfigureAwait(false);
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
        return _stream ??= JsonlAppendStream.Open(_filePath);
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
        catch (Exception ex)
        {
            // The file is where it was, so its count is what the next attempt
            // needs. A count that fails too belongs to the same incident.
            _linesWritten = CountLines(_filePath, out _);
            ReportMaintenanceFailure("roll", ex, onWorker: true);
            return;
        }

        // Only after a roll that worked: a failed move must not delete history.
        if (PruneArchives(onWorker: true))
            ReportMaintenanceRecovered(onWorker: true);
    }

    private void ReportMaintenanceFailure(string step, Exception error, bool onWorker)
    {
        if (_maintenanceFailures++ > 0) return;

        string fileName = Path.GetFileName(_filePath);
        OffWorkerReport.Run(onWorker, () => DeckleDiagnosticsSource.Log.RotationFailed(
            fileName, step, error.GetType().Name, error.Message));
    }

    private void ReportMaintenanceRecovered(bool onWorker)
    {
        int failures = _maintenanceFailures;
        if (failures == 0) return;
        _maintenanceFailures = 0;

        string fileName = Path.GetFileName(_filePath);
        OffWorkerReport.Run(onWorker, () => DeckleDiagnosticsSource.Log.RotationRecovered(fileName, failures));
    }


    private string ArchiveDirectory() =>
        Path.Combine(Path.GetDirectoryName(_filePath) ?? string.Empty, "archive");

    // Deletes the oldest generations beyond MaxArchives. Never throws: a log
    // that cannot trim itself must still keep writing, so a failure leaves the
    // remaining files alone and opens the maintenance incident. Returns whether
    // the pruning went through, which is what closes that incident.
    private bool PruneArchives(bool onWorker)
    {
        int? cap = _rotation?.MaxArchives;
        if (cap is null) return true;

        try
        {
            string archiveDirectory = ArchiveDirectory();
            if (!Directory.Exists(archiveDirectory)) return true;

            string fileName = Path.GetFileName(_filePath);
            List<(int Number, string Path)> generations =
                [.. EnumerateGenerations(archiveDirectory, fileName)];

            // The numeric suffix is the age: lowest is oldest. File dates move
            // when a file is copied or restored, so they are never consulted.
            generations.Sort(static (a, b) => a.Number.CompareTo(b.Number));

            int excess = generations.Count - cap.Value;
            if (excess <= 0) return true;

            int pruned = 0;
            Exception? deleteError = null;
            for (int i = 0; i < excess; i++)
            {
                try
                {
                    File.Delete(generations[i].Path);
                    pruned++;
                }
                catch (Exception ex)
                {
                    // Stop at the first failure so the survivors stay the
                    // newest contiguous run; the next roll tries again.
                    deleteError = ex;
                    break;
                }
            }

            if (pruned > 0)
            {
                int kept = generations.Count - pruned;
                OffWorkerReport.Run(onWorker, () => DeckleDiagnosticsSource.Log.ArchivesPruned(pruned, fileName, kept));
            }

            if (deleteError is null) return true;
            ReportMaintenanceFailure("prune", deleteError, onWorker);
            return false;
        }
        catch (Exception ex)
        {
            ReportMaintenanceFailure("prune", ex, onWorker);
            return false;
        }
    }

    // A listing that fails is not read as "no generation yet": the next number
    // would restart at 1 and the move, which overwrites, would erase the oldest
    // archive. The failure goes up to the roll, which reports it and leaves
    // the active file where it is.
    private static int NextGeneration(string archiveDirectory, string fileName)
    {
        int max = 0;
        foreach ((int number, _) in EnumerateGenerations(archiveDirectory, fileName))
        {
            if (number > max) max = number;
        }

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

    // A file that cannot be read counts as empty, so the sink goes on writing;
    // the error is handed back for the caller to report, because an unreadable
    // file taken for an empty one would never roll.
    private static long CountLines(string path, out Exception? error)
    {
        error = null;
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
        catch (Exception ex)
        {
            error = ex;
            return 0;
        }
    }
}
