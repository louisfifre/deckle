using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Deckle.Anytype.Mcp;

// The host clears the default logging providers (no console, no debug sink in
// a resident app), which also silenced Kestrel and the MCP SDK: an exception
// thrown inside the pipeline vanished. This provider hands their Warning and
// above to the Deckle trace. Only the category, the event name and the
// exception type cross over; the formatted message is never read, since
// framework text can quote request headers or tool arguments.
internal sealed class McpHostLogBridge : ILoggerProvider
{
    // Keyed by category and event name: the first occurrence is the Warning,
    // repeats stay Verbose with their running count.
    private readonly ConcurrentDictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private void Write(string category, EventId eventId, LogLevel level, Exception? exception)
    {
        string eventName = eventId.Name ?? eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string exType = exception?.GetType().Name ?? "";
        int occurrences = _occurrences.AddOrUpdate(category + "|" + eventName, 1, (_, count) => count + 1);
        if (occurrences == 1)
            DeckleAnytypeMcpSource.Log.HostInternalLog(category, eventName, level.ToString(), exType);
        else
            DeckleAnytypeMcpSource.Log.HostInternalLogRepeated(category, eventName, occurrences, exType);
    }

    private sealed class Logger(McpHostLogBridge owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            owner.Write(category, eventId, logLevel, exception);
        }
    }
}
