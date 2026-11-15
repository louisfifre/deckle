namespace Deckle.Projects;

// Reads what `scripts/menu.ps1 -List` prints: one line per action and per
// variant, five tab-separated fields: id, label, script (relative to
// scripts/), arguments, variant (empty when the action has none).
//
//   self-check.0        Self-check        self-check.ps1       (empty)  Debug
//   self-check.0        Self-check        self-check.ps1       (empty)  Release
//   deploy-prod.1       Roll back prod    deploy-prod.ps1      -Rollback (empty)
//
// Rows of one id are one entry; the order of an entry is the order of its first
// row, and so is the order of its variants (the first is the one `-Run` takes
// without `-Variant`, and the menu's own default). A line that does not have
// five fields, or whose id is empty, is not a row: a banner or a warning the
// shell printed before the listing is skipped, never an error.
public static class ScriptMenuParser
{
    private const int Fields = 5;

    public static IReadOnlyList<ScriptMenuEntry> Parse(string text)
    {
        var order = new List<string>();
        var first = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var variants = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string[] fields = line.Split('\t');
            if (fields.Length != Fields) continue;

            string id = fields[0].Trim();
            if (id.Length == 0) continue;

            if (!first.ContainsKey(id))
            {
                order.Add(id);
                first[id] = fields;
                variants[id] = [];
            }
            string variant = fields[4].Trim();
            if (variant.Length > 0 && !variants[id].Contains(variant, StringComparer.Ordinal)) variants[id].Add(variant);
        }

        return [.. order.Select(id => new ScriptMenuEntry(
            id, first[id][1].Trim(), first[id][2].Trim(), first[id][3].Trim(), variants[id]))];
    }
}
