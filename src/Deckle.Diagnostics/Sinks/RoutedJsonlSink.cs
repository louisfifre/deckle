using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Deckle.Diagnostics;

// Routed variant of JsonlSink. Same posture (one predicate, one kindLabel,
// line-by-line JSON serialization, file lock to avoid tearing), except the
// destination is no longer frozen at construction: a `pathResolver` computes it
// per event from its EventEntry. Allows one sink to spray the same event stream
// toward a dynamic tree of bucketed `corpus.jsonl` files (for example
// `corpus/raw/<tier>/corpus.jsonl` or `corpus/rewrite-<name>-<id>/corpus.jsonl`
// — see ADR-0006). A passive ILogSink: it does not subscribe to any
// EventSource, the dispatcher feeds it built entries.
//
// Why not inherit from JsonlSink. The corpus redesign brief decided: no
// inheritance. The "routed" mode is not extra behavior over the "flat" mode; it
// is another destination strategy. Exposing a mutable mode on the generic sink
// would make the API more fragile for zero gain (both types carry a handful of
// lines in common, and their controlled duplication avoids coupling their
// evolution cycles).
//
// Concurrency. Several simultaneously resolved paths can land on different
// files; a global lock would serialize writes that have no reason to block each
// other. The sink keeps a `ConcurrentDictionary<string, object>` indexed by
// concrete path: each path has its own lock, lazily allocated by the first
// event that writes to it. Parent directory creation piggybacks on that same
// `GetOrAdd`: the first event for a path calls `Directory.CreateDirectory`
// once.
//
// Safety. No path component validation here; it is the producer's (or
// resolver's) responsibility to sanitize dynamic segments before they cross.
// `CorpusPaths.Sanitize` is the intended producer-side utility for that.
public sealed class RoutedJsonlSink : ILogSink
{
    private readonly Func<EventEntry, string> _pathResolver;
    private readonly Func<EventEntry, bool> _predicate;
    private readonly string _kindLabel;
    private readonly ConcurrentDictionary<string, object> _pathLocks = new();

    private static readonly JsonWriterOptions _jsonOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // `pathResolver` — computes the absolute destination from the EventEntry.
    //                  Called for each event Wants accepted. Must return an
    //                  absolute, already-sanitized file path; a null/empty
    //                  return silently skips the event.
    // `kindLabel`    — value written under the JSONL "kind" key, aligned with
    //                  classic JsonlSink labels ("log", "latency", …).
    // `predicate`    — selects which events land in this sink. Receives the full
    //                  EventEntry to filter on name, level, keywords, or
    //                  payload. Exposed through Wants.
    public RoutedJsonlSink(
        Func<EventEntry, string> pathResolver,
        string kindLabel,
        Func<EventEntry, bool> predicate)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _kindLabel = kindLabel;
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool Wants(EventEntry entry) => _predicate(entry);

    public void Write(EventEntry entry)
    {
        string path;
        try { path = _pathResolver(entry); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            WriteLine(path, entry);
        }
        catch
        {
            // Same posture as JsonlSink: failed I/O must not crash the
            // dispatcher. Surfacing this kind of error (counter, dedicated
            // event) is a future observability task.
        }
    }

    private void WriteLine(string path, EventEntry entry)
    {
        byte[] jsonBytes;
        using (var ms = new MemoryStream(capacity: 256))
        {
            using (var writer = new Utf8JsonWriter(ms, _jsonOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("timestamp", entry.Timestamp.ToString("o", CultureInfo.InvariantCulture));
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

        // Lock by resolved path: lazy allocation through GetOrAdd guarantees
        // that a given path is always associated with the same object, and
        // therefore that only one thread writes to that file at a time. The
        // parent directory creation delta lives in the GetOrAdd factory: the
        // first event for a path calls Directory.CreateDirectory once, never
        // re-checked.
        object lockObj = _pathLocks.GetOrAdd(path, p =>
        {
            string? parent = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            return new object();
        });

        lock (lockObj)
        {
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(jsonBytes, 0, jsonBytes.Length);
            fs.WriteByte((byte)'\n');
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:                       writer.WriteNull(name); break;
            case string s:                   writer.WriteString(name, s); break;
            case bool b:                     writer.WriteBoolean(name, b); break;
            case int i:                      writer.WriteNumber(name, i); break;
            case long l:                     writer.WriteNumber(name, l); break;
            case short sh:                   writer.WriteNumber(name, sh); break;
            case byte by:                    writer.WriteNumber(name, by); break;
            case uint ui:                    writer.WriteNumber(name, ui); break;
            case ulong ul:                   writer.WriteNumber(name, ul); break;
            case ushort us:                  writer.WriteNumber(name, us); break;
            case sbyte sb:                   writer.WriteNumber(name, sb); break;
            case float f:                    writer.WriteNumber(name, f); break;
            case double d:                   writer.WriteNumber(name, d); break;
            case Guid g:                     writer.WriteString(name, g.ToString()); break;
            case DateTime dt:                writer.WriteString(name, dt.ToString("o", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto:         writer.WriteString(name, dto.ToString("o", CultureInfo.InvariantCulture)); break;
            // Fallback: EventSource only allows a restricted set of primitives
            // in [Event] signatures, so this branch should never be reached.
            default:                         writer.WriteString(name, value.ToString() ?? string.Empty); break;
        }
    }
}
