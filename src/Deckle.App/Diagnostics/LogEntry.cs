using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// UI-side wrapper around an EventEntry produced by the Deckle.Diagnostics
// listener. LogWindow consumes only LogEntry instances; the wrapper
// precomputes the displayed text (`HH:mm:ss.fff [SOURCE] message`) so
// ListView virtualization does not reformat on every row realization.
//
// The `Provider` → source label mapping ("Deckle-Whisp" → "WHISP",
// "Deckle-App" → "APP") follows the short uppercase convention inherited
// from legacy LogSource. It lives in Deckle.Diagnostics.LogLineFormatter so
// LogWindow and app.jsonl produce the same rendered line.
//
// `EventName` and `Level` are exposed as proxies because DataTemplateSelector
// routes its templates on these two properties: by event name for specialized
// telemetry rows (Latency / Corpus / Microphone), by BCL EventLevel for the
// rest.
public sealed class LogEntry
{
    public EventEntry Entry { get; }
    public string Text { get; }
    public string TimestampText { get; }
    public string SourceText { get; }
    public string MessageText { get; }
    public string EventName => Entry.EventName;
    public EventLevel Level => Entry.Level;

    public LogEntry(EventEntry entry)
    {
        Entry = entry;
        LogLineParts parts = LogLineFormatter.GetParts(entry);
        Text = parts.Text;
        TimestampText = parts.Timestamp;
        SourceText = $" [{parts.Source}] ";
        MessageText = parts.Message;
    }
}
