using System.Text;
using Deckle.Anytype;

namespace Deckle.Home;

// Pure body edits. The caller holds the Home write scope across GET/PATCH/read-back.
internal static class HomeBodyEditor
{
    public static string Edit(string current, string? appendText, HomeSectionEdit? section)
    {
        if (appendText is not null && section is not null)
            throw new ArgumentException("Choisis un ajout de texte ou une section à remplacer par mise à jour.");
        if (appendText is not null)
        {
            if (string.IsNullOrWhiteSpace(appendText))
                throw new ArgumentException("Le texte à ajouter ne peut pas être vide.");
            return string.IsNullOrEmpty(current) ? appendText.Trim()
                : current + (current.EndsWith("\n\n", StringComparison.Ordinal) ? "" : "\n\n") + appendText.Trim();
        }
        if (section is null || string.IsNullOrWhiteSpace(section.Heading))
            throw new ArgumentException("La section exige un titre existant.");
        var edit = MarkdownBody.ReplaceSection(current, section.Heading, section.Text);
        return edit.Status switch
        {
            MarkdownBody.EditStatus.Replaced => edit.Body,
            MarkdownBody.EditStatus.NotFound => throw new InvalidOperationException(
                $"Section « {section.Heading} » introuvable. Relis l'objet avant de choisir une section."),
            _ => throw new InvalidOperationException($"Section « {section.Heading} » ambiguë : plusieurs titres correspondent."),
        };
    }

    // Anytype escapes Markdown literals and reflows blank lines on export.
    // Compare the complete intended body, including untouched text, after those known normalizations.
    public static bool Matches(string expected, string actual) => Normalize(expected) == Normalize(actual);

    private static string Normalize(string value)
    {
        var lines = new List<string>();
        bool fenced = false;
        char fence = '\0';
        int fenceLength = 0;
        foreach (string raw in value.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string start = line.TrimStart(' ');
            int run = start.Length == 0 ? 0 : start.TakeWhile(c => c == start[0]).Count();
            bool marker = start.Length > 0 && start[0] is '`' or '~' && run >= 3
                && line.Length - start.Length <= 3;
            if (marker && (!fenced || (start[0] == fence && run >= fenceLength && start[run..].Trim().Length == 0)))
            {
                lines.Add(line.TrimEnd());
                if (!fenced) { fence = start[0]; fenceLength = run; }
                fenced = !fenced;
                continue;
            }
            if (fenced) { lines.Add(line); continue; }
            var unescaped = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] is '_' or '*' or '`' or '|' or '\\') i++;
                unescaped.Append(line[i]);
            }
            string normalized = unescaped.ToString().TrimEnd();
            if (normalized.Length > 0) lines.Add(normalized);
        }
        return string.Join("\n", lines);
    }
}
