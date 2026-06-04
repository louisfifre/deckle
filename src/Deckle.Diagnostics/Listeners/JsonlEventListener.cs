using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Diagnostics.Listeners;

// Persists Deckle.* events to a JSONL file. One listener instance per
// destination file; the App boot wires several of them — one for the
// general app log, one for latency rows, one for microphone telemetry,
// one for corpus rows — each with its own predicate that selects which
// events land in that file.
//
// The predicate sees the event's name (e.g. "LatencyRecorded") and the
// EventEntry. It returns true to write, false to skip. Wiring the
// per-file predicates lives in Deckle.Diagnostics.Telemetry, which
// also reads user gates (telemetry settings) and skips emission
// entirely when a gate is off.
//
// Schema. Two envelope shapes, selected per listener via `JsonlSchema`:
//   PayloadOnly (datasets) :
//     { "timestamp", "kind", "session", "payload": {...} }
//   SelfDescribing (app.jsonl) :
//     { "timestamp", "kind", "session", "provider", "event", "level",
//       "source", "message", "line", "payload": {...} }
// `timestamp` is ISO 8601 with offset to local time. `kind` is the
// channel label passed at construction. `session` is the process-local
// DeckleEventSource.SessionId. `payload` is the flat dictionary of
// [Event] parameters by their snake_case names. The SelfDescribing
// channel adds the event identity the LogWindow renders, so the file is
// a faithful, greppable mirror of the live journal rather than an
// anonymous payload — a parameter-less event keeps its provider/event/
// level instead of collapsing to an empty blob. See ADR-0007.
//
// Rotation. An optional `JsonlRotationPolicy` rolls the file once it reaches
// MaxLines lines into a monotonically-numbered generation under an `archive/`
// subfolder (app.jsonl → archive/app.jsonl.0001 → …). Generations accumulate;
// none is renamed or deleted. Datasets pass no policy and stay append-only.
// See ADR-0007.
//
// Threading. Write happens on the emitter thread, guarded by a per-
// listener lock so concurrent emissions don't tear lines and so a roll
// never races a write. The file is opened in append mode and flushed at
// every line, which lets a crash post-write keep the data on disk.
public sealed class JsonlEventListener : EventListener
{
    private readonly string _filePath;
    private readonly System.Func<EventEntry, bool> _predicate;
    private readonly System.Func<EventWrittenEventArgs, bool>? _preEntryDropPredicate;
    private readonly string _kindLabel;
    private readonly JsonlSchema _schema;
    private readonly JsonlRotationPolicy? _rotation;
    private readonly object _writeLock = new();
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    // Running line count of the active file, maintained under _writeLock so
    // the rotation check is a counter compare instead of re-reading the file
    // each line. Seeded by counting the active file's lines at construction
    // so a restart doesn't forget what was already written. Only meaningful
    // when _rotation is non-null.
    private long _linesWritten;

    private static readonly JsonWriterOptions _jsonOptions = new()
    {
        // Keep the JSON compact — one line per event.
        Indented = false,
        // Match legacy JsonlFileSink: don't escape forward slashes /
        // ampersands / unicode unnecessarily. The encoder defaults
        // are paranoid for HTML contexts; we write to a file the
        // user inspects directly.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // `filePath` — absolute path of the target JSONL file. The parent
    //              directory must already exist (caller responsibility).
    // `kindLabel` — the value written under the "kind" key. The legacy
    //              schema uses lowercase strings ("log", "latency",
    //              "corpus", "microphone"); pass the same value here.
    // `predicate` — selects which events land in this file. Receives
    //              the full EventEntry so it can filter on event name,
    //              keywords, or level.
    // `schema`    — envelope shape. PayloadOnly (default) for the frozen
    //              dataset channels; SelfDescribing for the app.jsonl
    //              journal.
    // `rotation`  — optional line-count roll policy. Null (default) leaves
    //              the file append-only without bound — correct for the
    //              datasets, never for the application journal.
    public JsonlEventListener(
        string filePath,
        string kindLabel,
        System.Func<EventEntry, bool> predicate,
        System.Func<EventWrittenEventArgs, bool>? preEntryDropPredicate = null,
        JsonlSchema schema = JsonlSchema.PayloadOnly,
        JsonlRotationPolicy? rotation = null)
    {
        _filePath = filePath;
        _kindLabel = kindLabel;
        _predicate = predicate;
        _preEntryDropPredicate = preEntryDropPredicate;
        _schema = schema;
        _rotation = rotation;

        if (_rotation is not null)
        {
            _linesWritten = CountLines(_filePath);
        }

        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.LogAlways, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle.", System.StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var preEntryDropPredicate = _preEntryDropPredicate;
        if (preEntryDropPredicate is not null)
        {
            try { if (preEntryDropPredicate(eventData)) return; }
            catch { /* A filter must never crash the listener. */ }
        }

        var entry = LogWindowEventListener.BuildEntry(eventData);
        if (!_predicate(entry)) return;

        try
        {
            WriteLine(entry);
        }
        catch
        {
            // I/O error must not crash the emitter — the legacy sink
            // had the same posture (swallow silently). Surfacing
            // this category of failure is a future improvement.
        }
    }

    private void WriteLine(EventEntry entry)
    {
        // Buffer the JSON in memory then append a single line. Using
        // a Utf8JsonWriter on a MemoryStream avoids partial writes on
        // serialisation error.
        byte[] jsonBytes;
        using (var ms = new MemoryStream(capacity: 256))
        {
            using (var writer = new Utf8JsonWriter(ms, _jsonOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("timestamp", entry.Timestamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteString("kind", _kindLabel);
                writer.WriteString("session", DeckleEventSource.SessionId);
                if (_schema == JsonlSchema.SelfDescribing)
                {
                    writer.WriteString("provider", entry.Provider);
                    writer.WriteString("event", entry.EventName);
                    writer.WriteString("level", entry.Level.ToString());
                    writer.WriteString("source", LogLineFormatter.MapSource(entry.Provider));
                    if (entry.FormattedMessage is null) writer.WriteNull("message");
                    else writer.WriteString("message", entry.FormattedMessage);
                    writer.WriteString("line", LogLineFormatter.Format(entry));
                }
                writer.WritePropertyName("payload");
                writer.WriteStartObject();
                foreach (var kv in entry.Payload)
                    WriteValue(writer, kv.Key, kv.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
            }
            jsonBytes = ms.ToArray();
        }

        lock (_writeLock)
        {
            // Roll before writing once the active file has reached the line
            // cap, so each generation holds at most MaxLines lines.
            if (_rotation is not null && _linesWritten >= _rotation.MaxLines)
            {
                RollFiles();
            }

            using (var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Write(jsonBytes, 0, jsonBytes.Length);
                fs.WriteByte((byte)'\n');
            }
            _linesWritten++;
        }
    }

    // Turns the active file into the next archive generation, kept under an
    // `archive/` subfolder next to it. The generation index is monotonic:
    // each roll takes the highest existing index + 1, zero-padded so a
    // directory listing sorts in chronological order (app.jsonl →
    // archive/app.jsonl.0001 → archive/app.jsonl.0002 → …). Nothing is ever
    // renamed or deleted — generations accumulate and the user prunes them at
    // will. Called under _writeLock with no stream open, so the move is safe.
    // Best-effort: if the move fails (an archive is held open by an external
    // reader), the active file is left in place and the line counter is
    // re-synced from disk so the next attempt waits for another MaxLines of
    // growth instead of retrying on every line.
    private void RollFiles()
    {
        var rotation = _rotation;
        if (rotation is null) return;

        try
        {
            string dir = Path.GetDirectoryName(_filePath) ?? string.Empty;
            string archiveDir = Path.Combine(dir, "archive");
            Directory.CreateDirectory(archiveDir);

            string fileName = Path.GetFileName(_filePath); // e.g. "app.jsonl"
            int next = NextGeneration(archiveDir, fileName);
            string target = Path.Combine(archiveDir, $"{fileName}.{next:D4}");

            if (File.Exists(_filePath)) File.Move(_filePath, target, overwrite: true);

            _linesWritten = 0L;
        }
        catch
        {
            _linesWritten = CountLines(_filePath);
        }
    }

    // Highest existing generation index in the archive folder, + 1 (1 when
    // empty). Recomputed at each roll rather than held in memory, so the
    // numbering continues correctly after a restart with no persisted state.
    // Rolls are rare (every MaxLines lines), so the directory scan is cheap.
    private static int NextGeneration(string archiveDir, string fileName)
    {
        int max = 0;
        string prefix = fileName + ".";
        try
        {
            foreach (string path in Directory.EnumerateFiles(archiveDir, prefix + "*"))
            {
                string suffix = Path.GetFileName(path).Substring(prefix.Length);
                if (int.TryParse(suffix, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int n) && n > max)
                    max = n;
            }
        }
        catch { /* fall back to 1 */ }
        return max + 1;
    }

    // Counts the lines (newline bytes) of a file on disk, streaming so a
    // large active journal isn't loaded into memory. Returns 0 for a missing
    // or unreadable file. Used to seed the line counter at construction and
    // to re-sync it after a failed roll.
    private static long CountLines(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0L;
            long count = 0L;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    if (buffer[i] == (byte)'\n') count++;
            }
            return count;
        }
        catch { return 0L; }
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:        writer.WriteNull(name); break;
            case string s:    writer.WriteString(name, s); break;
            case bool b:      writer.WriteBoolean(name, b); break;
            case int i:       writer.WriteNumber(name, i); break;
            case long l:      writer.WriteNumber(name, l); break;
            case short sh:    writer.WriteNumber(name, sh); break;
            case byte by:     writer.WriteNumber(name, by); break;
            case uint ui:     writer.WriteNumber(name, ui); break;
            case ulong ul:    writer.WriteNumber(name, ul); break;
            case ushort us:   writer.WriteNumber(name, us); break;
            case sbyte sb:    writer.WriteNumber(name, sb); break;
            case float f:     writer.WriteNumber(name, f); break;
            case double d:    writer.WriteNumber(name, d); break;
            case System.Guid g: writer.WriteString(name, g.ToString()); break;
            case System.DateTime dt: writer.WriteString(name, dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)); break;
            case System.DateTimeOffset dto: writer.WriteString(name, dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture)); break;
            // Fallback for unexpected types: stringify. EventSource
            // only allows a limited set of primitives in [Event]
            // signatures, so we should never reach here in practice.
            default:          writer.WriteString(name, value.ToString() ?? string.Empty); break;
        }
    }
}
