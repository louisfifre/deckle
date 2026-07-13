using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Diagnostics.Logging;

// A filter is an inclusion lens over EventEntry. Empty dimensions accept every
// value; values inside one dimension are OR-ed, while active dimensions are
// AND-ed. The type is UI-agnostic so the live viewer and app.jsonl settings can
// own independent instances without sharing state or persistence policy.
public sealed class LogFilterSelection
{
    private readonly HashSet<EventLevel> _severities = [];
    private readonly HashSet<string> _modules = new(StringComparer.Ordinal);
    private readonly HashSet<Keywords> _categories = [];

    public event EventHandler? Changed;

    public int Count => _severities.Count + _modules.Count + _categories.Count;
    public bool IsEmpty => Count == 0;

    public IReadOnlyCollection<EventLevel> Severities => _severities;
    public IReadOnlyCollection<string> Modules => _modules;
    public IReadOnlyCollection<Keywords> Categories => _categories;

    public bool Contains(LogFilterToken token) => token.Dimension switch
    {
        LogFilterDimension.Severity => TryParseSeverity(token.Value, out var level)
                                    && _severities.Contains(level),
        LogFilterDimension.Module => _modules.Contains(token.Value),
        LogFilterDimension.Category => TryParseCategory(token.Value, out var category)
                                    && _categories.Contains(category),
        _ => false,
    };

    public bool Add(LogFilterToken token)
    {
        bool added = token.Dimension switch
        {
            LogFilterDimension.Severity => TryParseSeverity(token.Value, out var level)
                                        && _severities.Add(level),
            LogFilterDimension.Module => !string.IsNullOrWhiteSpace(token.Value)
                                      && _modules.Add(token.Value),
            LogFilterDimension.Category => TryParseCategory(token.Value, out var category)
                                        && _categories.Add(category),
            _ => false,
        };

        if (added) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public bool Remove(LogFilterToken token)
    {
        bool removed = token.Dimension switch
        {
            LogFilterDimension.Severity => TryParseSeverity(token.Value, out var level)
                                        && _severities.Remove(level),
            LogFilterDimension.Module => _modules.Remove(token.Value),
            LogFilterDimension.Category => TryParseCategory(token.Value, out var category)
                                        && _categories.Remove(category),
            _ => false,
        };

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Clear()
    {
        if (IsEmpty) return;
        _severities.Clear();
        _modules.Clear();
        _categories.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Matches(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_severities.Count > 0 && !_severities.Contains(Normalize(entry.Level)))
            return false;

        if (_modules.Count > 0 && !_modules.Contains(entry.Provider))
            return false;

        if (_categories.Count > 0)
        {
            var entryKeywords = (Keywords)(long)entry.Keywords;
            bool categoryMatch = false;
            foreach (Keywords category in _categories)
            {
                if ((entryKeywords & category) == 0) continue;
                categoryMatch = true;
                break;
            }

            if (!categoryMatch) return false;
        }

        return true;
    }

    public IEnumerable<LogFilterToken> GetTokens()
    {
        foreach (EventLevel level in SeverityOrder)
            if (_severities.Contains(level))
                yield return new(LogFilterDimension.Severity, level.ToString());

        foreach (string provider in _modules.Order(StringComparer.Ordinal))
            yield return new(LogFilterDimension.Module, provider);

        foreach (Keywords category in CategoryOrder)
            if (_categories.Contains(category))
                yield return new(LogFilterDimension.Category, category.ToString());
    }

    public static IReadOnlyList<EventLevel> SeverityOrder { get; } =
    [
        EventLevel.Verbose,
        EventLevel.Informational,
        EventLevel.Warning,
        EventLevel.Error,
        EventLevel.Critical,
    ];

    public static IReadOnlyList<Keywords> CategoryOrder { get; } =
    [
        Keywords.Lifecycle,
        Keywords.Capture,
        Keywords.Pipeline,
        Keywords.Push,
        Keywords.Heartbeat,
        Keywords.Windowing,
        Keywords.Threading,
        Keywords.Theme,
        Keywords.Resource,
        Keywords.Network,
    ];

    private static EventLevel Normalize(EventLevel level)
        => level == EventLevel.LogAlways ? EventLevel.Informational : level;

    private static bool TryParseSeverity(string value, out EventLevel level)
    {
        bool parsed = Enum.TryParse(value, ignoreCase: false, out level);
        return parsed && SeverityOrder.Contains(level);
    }

    private static bool TryParseCategory(string value, out Keywords category)
    {
        bool parsed = Enum.TryParse(value, ignoreCase: false, out category);
        return parsed && CategoryOrder.Contains(category);
    }
}
