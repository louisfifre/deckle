using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

// Precomputes the displayed text so ListView virtualization does not reformat
// on every row realization. The rendering lives in LogLineFormatter so LogWindow
// and app.jsonl produce the same line.
public sealed class LogEntry
{
    public EventEntry Entry { get; }
    public string Text { get; }
    public string TimestampText { get; }
    public string SeverityText { get; }
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
        SeverityText = entry.Level switch
        {
            EventLevel.Verbose       => "VERBOSE",
            EventLevel.Informational => "INFO",
            EventLevel.Warning       => "WARNING",
            EventLevel.Error         => "ERROR",
            EventLevel.Critical      => "CRITICAL",
            EventLevel.LogAlways     => "INFO",
            _                        => entry.Level.ToString().ToUpperInvariant(),
        };
        SourceText = parts.Source;
        MessageText = parts.Message;
    }
}
