using System.Globalization;

namespace Deckle.Diagnostics;

// Canonical live-journal rendering shared by the LogWindow and the
// app.jsonl writer. Keeping the source tag and line format here avoids
// the historical drift where the window was readable but the file only
// carried an opaque payload blob.
public static class LogLineFormatter
{
    public static string Format(EventEntry entry)
    {
        string source = MapSource(entry.Provider);
        string message = entry.FormattedMessage ?? entry.EventName;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} [{1}] {2}",
            entry.Timestamp, source, message);
    }

    public static string MapSource(string providerName)
    {
        const string prefix = "Deckle.";

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
