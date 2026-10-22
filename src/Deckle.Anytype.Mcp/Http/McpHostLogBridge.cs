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
    // repeats stay Verbose with their running count, and the journal gets the count
    // at every power of ten and once more when the host's logging closes.
    private readonly ConcurrentDictionary<string, Problem> _problems = new(StringComparer.Ordinal);

    // Mutated only through the dictionary's AddOrUpdate, which may run its factory
    // more than once under contention: the record is immutable and replaced.
    private sealed record Problem(string Category, string EventName, string ExType, int Occurrences, int Counted);

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    // The host disposes its service provider, and with it this provider, when it
    // stops: the last chance to say how often each problem happened.
    public void Dispose()
    {
        foreach (Problem problem in _problems.Values)
        {
            if (problem.Occurrences > 1 && problem.Occurrences != problem.Counted)
            {
                DeckleAnytypeMcpSource.Log.HostInternalLogCounted(
                    problem.Category, problem.EventName, problem.Occurrences, problem.ExType);
            }
        }
    }

    private void Write(string category, EventId eventId, LogLevel level, Exception? exception)
    {
        string eventName = eventId.Name ?? eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string exType = exception?.GetType().Name ?? "";
        Problem problem = _problems.AddOrUpdate(
            category + "|" + eventName,
            _ => new Problem(category, eventName, exType, 1, 0),
            (_, known) => known with { Occurrences = known.Occurrences + 1 });
        if (problem.Occurrences == 1)
        {
            DeckleAnytypeMcpSource.Log.HostInternalLog(category, eventName, level.ToString(), exType);
            return;
        }

        DeckleAnytypeMcpSource.Log.HostInternalLogRepeated(category, eventName, problem.Occurrences, exType);
        if (IsPowerOfTen(problem.Occurrences))
        {
            DeckleAnytypeMcpSource.Log.HostInternalLogCounted(category, eventName, problem.Occurrences, exType);
            _problems.TryUpdate(
                category + "|" + eventName,
                problem with { Counted = problem.Occurrences },
                problem);
        }
    }

    private static bool IsPowerOfTen(int value)
    {
        if (value < 10) return false;
        while (value % 10 == 0) value /= 10;
        return value == 1;
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
