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
// Schema. The JSON line layout reproduces the legacy schema:
//   { "timestamp": "...", "kind": "...", "session": "...", "payload": {...} }
// `timestamp` is ISO 8601 with offset to local time, matching legacy
// JsonlFileSink output. `kind` is derived from the event name (the
// provider can override via the gate by emitting a dedicated event
// name). `session` is the process-local DeckleEventSource.SessionId.
// `payload` is the flat dictionary of [Event] parameters by their
// snake_case names — identical to legacy JsonPropertyName output.
//
// Threading. Write happens on the emitter thread, guarded by a per-
// listener lock so concurrent emissions don't tear lines. The
// StreamWriter is opened in append mode and flushed at every line —
// same posture as JsonlFileSink, which lets a crash post-write keep
// the data on disk.
public sealed class JsonlEventListener : EventListener
{
    private readonly string _filePath;
    private readonly System.Func<EventEntry, bool> _predicate;
    private readonly string _kindLabel;
    private readonly object _writeLock = new();
    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

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
    public JsonlEventListener(
        string filePath,
        string kindLabel,
        System.Func<EventEntry, bool> predicate)
    {
        _filePath = filePath;
        _kindLabel = kindLabel;
        _predicate = predicate;

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
            using var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(jsonBytes, 0, jsonBytes.Length);
            fs.WriteByte((byte)'\n');
        }
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
