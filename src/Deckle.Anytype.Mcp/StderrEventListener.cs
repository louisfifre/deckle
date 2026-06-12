using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using Deckle.Diagnostics;

namespace Deckle.Anytype.Mcp;

// Drains every Deckle-* EventSource to stderr, one line per event. In the
// MCP host stdout is reserved for JSON-RPC, so this is the only log sink;
// the parent process (the MCP client) collects stderr.
//
// Line shape: "HH:mm:ss.fff LEVEL SOURCE message". The level is part of
// the line — LogLineFormatter omits it — so we format locally and reuse
// only LogLineFormatter.MapSource to keep the source tag identical to the
// rest of Deckle. The message is the [Event(Message=…)] template filled
// with the payload, falling back to the bare event name.
//
// OnEventSourceCreated fires from the base constructor before this
// derived constructor's fields are initialised, so providers seen during
// base init are parked in _earlySources and enabled once _ready flips.
public sealed class StderrEventListener : EventListener
{
    private readonly TextWriter _err;
    private readonly object _writeLock = new();

    private readonly List<EventSource> _earlySources = new();
    private bool _ready;

    public StderrEventListener(TextWriter err)
    {
        _err = err;
        lock (_earlySources)
        {
            _ready = true;
            foreach (var src in _earlySources)
                EnableEvents(src, EventLevel.Verbose, EventKeywords.All);
            _earlySources.Clear();
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is null) return;
        if (!eventSource.Name.StartsWith("Deckle-", StringComparison.Ordinal)) return;

        lock (_earlySources)
        {
            if (!_ready)
            {
                _earlySources.Add(eventSource);
                return;
            }
        }
        EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        string? provider = eventData.EventSource.Name;
        if (provider is null) return;

        string source = LogLineFormatter.MapSource(provider);
        string level = LevelTag(eventData.Level);
        string message = FormatMessage(eventData);

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} {1} {2} {3}",
            DateTimeOffset.Now, level, source, message);

        // Serialise writes: OnEventWritten fires on the emitting thread, so
        // concurrent gestures could interleave lines otherwise.
        lock (_writeLock)
        {
            _err.WriteLine(line);
        }
    }

    private static string FormatMessage(EventWrittenEventArgs e)
    {
        var values = e.Payload;
        if (!string.IsNullOrEmpty(e.Message) && values is not null)
        {
            try
            {
                var arr = new object?[values.Count];
                for (int i = 0; i < values.Count; i++) arr[i] = values[i];
                return string.Format(CultureInfo.InvariantCulture, e.Message, arr);
            }
            catch
            {
                // A malformed template must not break the log pipeline; fall
                // through to the bare event name.
            }
        }
        return e.EventName ?? "(unnamed)";
    }

    private static string LevelTag(EventLevel level) => level switch
    {
        EventLevel.Critical => "CRIT",
        EventLevel.Error => "ERR ",
        EventLevel.Warning => "WARN",
        EventLevel.Informational => "INFO",
        EventLevel.Verbose => "VERB",
        _ => "LOG ",
    };
}
