namespace Deckle.Projects;

// The few lines of TOML that Cargo.toml and pyproject.toml are read with: a
// table header and a `version = "x"` key under it. A real TOML parser would
// add a package to read one string, and a manifest that holds anything the
// small reader does not follow (multi-line strings that mimic a header, say)
// gives no version, which the page shows as no source, never as an error.
internal static class TomlVersion
{
    // The value of `key = "..."` found in the first of the given tables that
    // has one, tried in order. Null when none has it, when the value is not a
    // plain string (`version.workspace = true`, `version = { workspace = true }`)
    // or when it is built from variables.
    public static string? Read(string text, string key, params string[] tables)
    {
        foreach (string table in tables)
        {
            string? value = ReadIn(text, table, key);
            if (value is not null) return value;
        }
        return null;
    }

    private static string? ReadIn(string text, string table, string key)
    {
        string? current = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            if (line[0] == '[')
            {
                // "[package]" and "[[bin]]" alike open a table; only the first
                // is ever one of ours.
                current = line.StartsWith("[[", StringComparison.Ordinal) ? null : TableName(line);
                continue;
            }
            if (current is null || !string.Equals(current, table, StringComparison.Ordinal)) continue;

            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            string name = Unquote(line[..equals].Trim());
            if (!string.Equals(name, key, StringComparison.Ordinal)) continue;

            return VersionText.Plain(QuotedValue(line[(equals + 1)..].Trim()));
        }
        return null;
    }

    private static string TableName(string header)
    {
        int close = header.IndexOf(']');
        string inside = close > 0 ? header[1..close] : header[1..];
        // "tool . poetry" is the same table as "tool.poetry".
        return string.Join('.', inside.Split('.').Select(part => Unquote(part.Trim())));
    }

    private static string? QuotedValue(string value)
    {
        if (value.Length < 2) return null;
        char quote = value[0];
        if (quote is not ('"' or '\'')) return null;
        int end = value.IndexOf(quote, 1);
        return end < 0 ? null : value[1..end];
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && (text[0] is '"' or '\'') && text[^1] == text[0] ? text[1..^1] : text;

    // A '#' outside a quoted string starts a comment.
    private static string StripComment(string line)
    {
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == '#') return line[..i];
        }
        return line;
    }
}
