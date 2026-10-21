using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Deckle.Diagnostics;

// Canonical live-journal rendering shared by the LogWindow and the
// app.jsonl writer, so the window and the file never diverge.
public static class LogLineFormatter
{
    private const string ProviderPrefix = "Deckle-";

    // Only the providers whose suffix does not read as a name once split at
    // its case changes. Every other Deckle provider takes the fallback, so a
    // new module reads acceptably before it earns an entry here. The labels
    // match the LogWindow module filter, so a person filters on the word the
    // column shows.
    private static readonly FrozenDictionary<string, string> DisplayNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Deckle"] = "App",
            ["Deckle-App"] = "App",
            ["Deckle-Whisp"] = "Dictation",
            ["Deckle-Llm"] = "LLM",
            ["Deckle-Vad"] = "VAD",
            ["Deckle-AnytypeMcp"] = "Anytype MCP",
            ["Deckle-SettingsUx"] = "Settings UX",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static LogLineParts GetParts(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new LogLineParts(
            entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DisplayName(entry.Provider),
            entry.FormattedMessage ?? entry.EventName);
    }

    public static string Format(EventEntry entry) => GetParts(entry).Text;

    // The name a person reads in the module column and the text line. It is
    // invariant English on purpose: the same line lands in app.jsonl.
    public static string DisplayName(string providerName)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        if (DisplayNames.TryGetValue(providerName, out string? name))
            return name;

        // A provider outside Deckle keeps its own name: rewriting it would
        // hide what emitted the line.
        if (!providerName.StartsWith(ProviderPrefix, StringComparison.Ordinal)
            || providerName.Length == ProviderPrefix.Length)
            return providerName;

        // "PrecisionScroll" reads "Precision Scroll", "Lighting.Ambient"
        // reads "Lighting Ambient".
        ReadOnlySpan<char> suffix = providerName.AsSpan(ProviderPrefix.Length);
        var builder = new StringBuilder(suffix.Length + 4);
        for (int i = 0; i < suffix.Length; i++)
        {
            char c = suffix[i];
            if (c is '.' or '-' or '_')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != ' ') builder.Append(' ');
                continue;
            }
            if (i > 0 && char.IsUpper(c) && char.IsLower(suffix[i - 1]))
                builder.Append(' ');
            builder.Append(c);
        }
        return builder.ToString().TrimEnd();
    }

    // The stable machine token of the app.jsonl "source" field. It stays
    // apart from DisplayName so a reader parsing that field keeps the
    // vocabulary it already greps for.
    public static string MapSource(string providerName)
    {
        if (string.Equals(providerName, "Deckle", StringComparison.Ordinal))
            return "APP";

        if (providerName.StartsWith(ProviderPrefix, StringComparison.Ordinal))
        {
            string suffix = providerName.Substring(ProviderPrefix.Length);
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
