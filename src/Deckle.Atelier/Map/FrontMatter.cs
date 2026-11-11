namespace Deckle.Atelier;

// The YAML block between two "---" lines at the top of a skill, a rule or a
// subagent. Only what the map asks is read: top-level keys with a plain value,
// a folded or literal block (description: >), or a list, whose presence is
// enough. Not a YAML parser; a key it cannot read is simply absent.
internal sealed class FrontMatter
{
    public static readonly FrontMatter None = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, string> _values;

    private FrontMatter(Dictionary<string, string> values) => _values = values;

    public bool Has(string key) => _values.ContainsKey(key);

    public string? this[string key] => _values.TryGetValue(key, out string? value) && value.Length > 0 ? value : null;

    public static FrontMatter Parse(string text)
    {
        string[] lines = TextLines.Split(text);
        if (lines.Length < 2 || lines[0].Trim() != "---") return None;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? blockKey = null;
        var block = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim() == "---")
            {
                Close();
                return new FrontMatter(values);
            }
            bool indented = line.Length > 0 && char.IsWhiteSpace(line[0]);
            if (indented || line.TrimStart().StartsWith('-'))
            {
                if (blockKey is not null) block.Add(line.Trim());
                continue;
            }
            Close();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (value is "" or ">" or "|" or ">-" or "|-")
            {
                blockKey = key;
                values[key] = "";
            }
            else
            {
                values[key] = Unquote(value);
            }
        }
        // No closing "---": not front matter.
        return None;

        void Close()
        {
            if (blockKey is null) return;
            string joined = string.Join(' ', block.Where(l => l.Length > 0 && !l.StartsWith('-')));
            // A list (paths:, globs:) keeps a marker so Has() and a non-null value both hold.
            values[blockKey] = joined.Length > 0 ? joined : block.Count > 0 ? "[list]" : "";
            blockKey = null;
            block.Clear();
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0] ? value[1..^1] : value;
}
