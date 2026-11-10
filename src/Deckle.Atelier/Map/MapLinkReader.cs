using System.Text.RegularExpressions;

namespace Deckle.Atelier;

internal enum MapHitKind
{
    // The target of a Markdown link, as written.
    Link,

    // The path after @ in a file whose imports an agent expands.
    Import,

    // A token that looks like a script's path or file name.
    ScriptPath,

    // A word that may be a skill's or a server's name.
    Name,
}

internal readonly record struct MapHit(MapHitKind Kind, string Value, int Line);

// Reads what a text cites, without knowing yet which items exist; the
// builder resolves each hit against the inventory and drops the ones that
// name nothing. Markdown links and imports are read outside code, as Claude
// Code reads imports ("Import parsing skips Markdown code spans and fenced
// code blocks"); a script is most often named inside code, so script paths
// and names are read everywhere.
internal static partial class MapLinkReader
{
    public static IEnumerable<MapHit> Read(string text, bool isScript, bool expandsImports)
    {
        string[] lines = TextLines.Split(text);
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int number = i + 1;

            foreach (Match m in ScriptPath().Matches(line)) yield return new(MapHitKind.ScriptPath, m.Groups[1].Value, number);
            foreach (Match m in McpToolName().Matches(line)) yield return new(MapHitKind.Name, m.Groups[1].Value, number);
            if (isScript) continue;

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            foreach (Match m in SlashCommand().Matches(line)) yield return new(MapHitKind.Name, m.Groups[1].Value, number);
            if (inFence) continue;

            foreach (Match m in InlineCode().Matches(line))
            {
                string code = m.Groups[2].Value.Trim().TrimStart('/');
                if (code.Length > 0) yield return new(MapHitKind.Name, code, number);
            }
            string prose = InlineCode().Replace(line, " ");
            foreach (Match m in MarkdownLink().Matches(prose)) yield return new(MapHitKind.Link, m.Groups[1].Value, number);
            foreach (Match m in ReferenceDefinition().Matches(prose)) yield return new(MapHitKind.Link, m.Groups[1].Value, number);
            if (!expandsImports) continue;
            foreach (Match m in ImportPath().Matches(prose))
            {
                string path = m.Groups[1].Value.TrimEnd('.', ':', ',', ';', '!', '?');
                if (path.Length > 0) yield return new(MapHitKind.Import, path, number);
            }
        }
    }

    // A link target as a path inside the folder: web addresses, mail and
    // in-page anchors are not, a fragment or a query is dropped.
    public static string? LinkPath(string target)
    {
        if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
        int cut = target.IndexOfAny(['#', '?']);
        if (cut >= 0) target = target[..cut];
        if (target.Length == 0) return null;
        try { return Uri.UnescapeDataString(target); }
        catch (UriFormatException) { return target; }
    }

    [GeneratedRegex(@"(`+)(.+?)\1")]
    private static partial Regex InlineCode();

    // ](target) with an optional <...> and title; the [text] part is not needed.
    [GeneratedRegex(@"\]\(\s*<?([^)\s>]+)>?[^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^\s{0,3}\[[^\]]+\]:\s*<?([^\s>]+)>?")]
    private static partial Regex ReferenceDefinition();

    // @path at the start of a line or after a space; an e-mail address has a
    // letter before its @ and is not read.
    [GeneratedRegex(@"(?<![^\s(])@([^\s`'""()\[\]{}<>,;@]+)")]
    private static partial Regex ImportPath();

    [GeneratedRegex(@"(?<![\w./\\-])((?:[\w.\-]+[/\\])*[\w\-][\w.\-]*\.(?:ps1|cmd|bat|sh|bash|zsh|py|js|mjs|cjs|ts))(?![\w-])", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptPath();

    // /name, the way a skill or a command is called; not a path segment.
    [GeneratedRegex(@"(?<![\w/.:\\-])/([a-z0-9][a-z0-9_-]*)(?![\w/\\-]|\.\w)", RegexOptions.IgnoreCase)]
    private static partial Regex SlashCommand();

    // mcp__server__tool, the name Claude Code gives a server's tool.
    [GeneratedRegex(@"\bmcp__([A-Za-z0-9-]+(?:_[A-Za-z0-9-]+)*)__")]
    private static partial Regex McpToolName();
}
