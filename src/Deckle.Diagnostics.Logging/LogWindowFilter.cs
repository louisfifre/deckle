using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Shared projection for the live application journal. The LogWindow uses
// it for its in-memory visible list; app.jsonl uses the same predicate so
// disk persistence follows the chosen level family for new events.
public static class LogWindowFilter
{
    public static bool IsStructuredTelemetry(string eventName)
        => eventName == "LatencyRecorded"
        || eventName == "CorpusAsrRecorded"
        || eventName == "CorpusRewriteRecorded"
        || eventName == "MicrophoneTelemetryRecorded";

    public static bool IsVisible(EventEntry entry, LogWindowVisibilityMode mode)
        => IsVisible(entry.Level, entry.EventName, mode);

    public static bool IsVisible(EventLevel level, string eventName, LogWindowVisibilityMode mode)
    {
        return mode switch
        {
            LogWindowVisibilityMode.All => true,
            LogWindowVisibilityMode.Activity => !IsStructuredTelemetry(eventName)
                                             && level != EventLevel.Verbose
                                             && level != EventLevel.LogAlways,
            LogWindowVisibilityMode.Alerts => !IsStructuredTelemetry(eventName)
                                           && (level == EventLevel.Warning
                                            || level == EventLevel.Error
                                            || level == EventLevel.Critical),
            _ => true,
        };
    }
}
