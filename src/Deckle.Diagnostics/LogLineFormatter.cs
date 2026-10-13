using System.Globalization;

namespace Deckle.Diagnostics;

// Canonical live-journal rendering shared by the LogWindow and the
// app.jsonl writer, so the window and the file never diverge.
public static class LogLineFormatter
{
    public static LogLineParts GetParts(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new LogLineParts(
            entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            MapSource(entry.Provider),
            entry.FormattedMessage ?? entry.EventName);
    }

    public static string Format(EventEntry entry) => GetParts(entry).Text;

    public static string MapSource(string providerName)
    {
        const string prefix = "Deckle-";

        if (string.Equals(providerName, "Deckle", StringComparison.Ordinal))
            return "APP";

        if (providerName.StartsWith(prefix, StringComparison.Ordinal))
        {
            string suffix = providerName.Substring(prefix.Length);
            if (string.Equals(suffix, "App", StringComparison.Ordinal))
                return "APP";
            return suffix.ToUpperInvariant();
        }

        return providerName.ToUpperInvariant();
    }
}

public readonly record struct LogLineParts(
    string Timestamp,
    string Source,
    string Message)
{
    public string Text => string.Concat(Timestamp, " [", Source, "] ", Message);
}
