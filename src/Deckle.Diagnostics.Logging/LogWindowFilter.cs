using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// Display projection for the live LogWindow: it computes the in-memory
// visible list from the chosen level family. It is a viewer lens only —
// app.jsonl does NOT route through it, so the disk journal never depends on
// what the window happens to be showing.
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
