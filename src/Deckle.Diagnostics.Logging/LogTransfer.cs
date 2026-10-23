using System.Text;

namespace Deckle.Diagnostics.Logging;

public enum LogTransferScope
{
    All,
    Filtered,
    Selection,
}

// Resolves one chronological snapshot for both Copy and Save. Filtered is
// evaluated against the full in-memory log at invocation time, not against
// the viewport or a debounce-stale projection.
public static class LogTransferScopeResolver
{
    public static IReadOnlyList<T> Resolve<T>(
        LogTransferScope scope,
        IEnumerable<T> all,
        Func<T, bool> matchesFilter,
        IReadOnlySet<T> selection)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(matchesFilter);
        ArgumentNullException.ThrowIfNull(selection);

        return scope switch
        {
            LogTransferScope.All => all.ToArray(),
            LogTransferScope.Filtered => all.Where(matchesFilter).ToArray(),
            LogTransferScope.Selection => all.Where(selection.Contains).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };
    }
}

public static class LogTransferText
{
    public static string Format<T>(
        IEnumerable<T> entries,
        Func<T, string> getCompleteText)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(getCompleteText);

        // A two-line row carries "\n" inside its text while AppendLine ends
        // each row with the platform newline; one ending for the whole block
        // keeps a saved file from mixing the two.
        var text = new StringBuilder();
        foreach (T entry in entries)
            text.AppendLine(getCompleteText(entry).ReplaceLineEndings());
        return text.ToString();
    }
}
