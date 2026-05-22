using Deckle.Diagnostics;
using Deckle.Logging;

namespace Deckle.Diagnostics;

// Bridge sink that pipes every Deckle.* EventSource emission into the
// legacy TelemetryService so the existing LogWindow surface (still
// owned by the legacy pipeline) shows the new events alongside the
// old ones. Used only during the migration window — once Wave 6 lifts
// the legacy logging, LogWindow consumes Deckle.Diagnostics directly
// and this bridge disappears.
//
// Mapping rules.
//   provider "Deckle.Chrono" → source label "CHRONO" (uppercase, last
//     segment of the dotted name — matches the LogSource convention of
//     short uppercase tags). Special case: "Deckle" maps to "APP".
//   formatted message → message text. Null falls back to "<EventName>"
//     so the row carries at least a useful label.
//   EventLevel → LogLevel:
//     Critical / Error → Error
//     Warning          → Warning
//     Informational    → Info
//     Verbose          → Verbose
//     LogAlways        → Info (defensive — should not appear on real events)
internal sealed class LegacyLogWindowSink : ILogWindowSink
{
    public void Write(EventEntry entry)
    {
        string source = MapSource(entry.Provider);
        string message = entry.FormattedMessage ?? entry.EventName;
        LogLevel level = MapLevel(entry.Level);
        TelemetryService.Instance.Log(source, message, level, feedback: null);
    }

    private static string MapSource(string providerName)
    {
        // Strip the "Deckle." prefix and uppercase the remainder. A
        // provider that's exactly "Deckle" — none currently, but
        // future "DeckleAppSource" might claim that name — maps to
        // "APP" to align with the legacy LogSource.App constant.
        const string prefix = "Deckle.";
        if (string.Equals(providerName, "Deckle", System.StringComparison.Ordinal))
            return "APP";
        if (providerName.StartsWith(prefix, System.StringComparison.Ordinal))
            return providerName.Substring(prefix.Length).ToUpperInvariant();
        return providerName.ToUpperInvariant();
    }

    private static LogLevel MapLevel(System.Diagnostics.Tracing.EventLevel level) => level switch
    {
        System.Diagnostics.Tracing.EventLevel.Critical      => LogLevel.Error,
        System.Diagnostics.Tracing.EventLevel.Error         => LogLevel.Error,
        System.Diagnostics.Tracing.EventLevel.Warning       => LogLevel.Warning,
        System.Diagnostics.Tracing.EventLevel.Informational => LogLevel.Info,
        System.Diagnostics.Tracing.EventLevel.Verbose       => LogLevel.Verbose,
        _                                                   => LogLevel.Info,
    };
}
