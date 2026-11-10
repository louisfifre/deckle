namespace Deckle.Atelier.Views;

// How the map names and draws each kind of item. The kinds are told apart at
// a glance by a glyph in front of the name and by the Type column (spec, Map;
// board 12.8b). The glyphs: an open book for what an agent reads (the boards'
// page with lines rendered like the plain page in Segoe Fluent Icons, so the
// two kinds looked alike), a sparkle for a skill, a connected screen for a
// server, a prompt for a script, a plain page for the standard files. Code points,
// written as escapes so the code reads without the font.
internal static class MapKindText
{
    public static string Name(MapItemKind kind) => AtelierText.Get($"Map_Kind_{kind}");

    // The filter box's label (12.8b: "Fichiers de contexte", "Skills", ...).
    public static string Plural(MapItemKind kind) => AtelierText.Get($"Map_Kinds_{kind}");

    // The empty state's list of what was looked for (12.14).
    public static string LookedFor() =>
        AtelierText.Format("Map_LookedFor_Format", string.Join(
            AtelierText.Get("Map_LookedFor_Separator"),
            MapInventory.KindsLookedFor.Select(k => AtelierText.Get($"Map_LookedFor_{k}"))));

    public static string Glyph(MapItemKind kind) => kind switch
    {
        MapItemKind.ContextFile => "",
        MapItemKind.Skill => "",
        MapItemKind.McpServer => "",
        MapItemKind.Script => "",
        _ => "",
    };

    public static string Loading(MapLoading loading) => AtelierText.Get($"Map_Loading_{loading}");

    // Product names, the same in every language.
    public static string? Readers(MapReaders readers)
    {
        if (readers == MapReaders.None) return null;
        var names = new List<string>();
        if (readers.HasFlag(MapReaders.ClaudeCode)) names.Add("Claude Code");
        if (readers.HasFlag(MapReaders.Codex)) names.Add("Codex");
        if (readers.HasFlag(MapReaders.GitHubCopilot)) names.Add("GitHub Copilot");
        if (readers.HasFlag(MapReaders.GeminiCli)) names.Add("Gemini CLI");
        if (readers.HasFlag(MapReaders.Cursor)) names.Add("Cursor");
        return string.Join(", ", names);
    }

    // Where the item sits, as the panel's kind line shows it: a skill by its
    // folder, since its name is the folder's; any other item by its file.
    public static string Place(MapItem item) =>
        item.Kind == MapItemKind.Skill && item.RelativePath.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
            ? item.RelativePath[..^"/SKILL.md".Length]
            : item.RelativePath;

    public static string Count(int value) => value.ToString("N0", AtelierText.Culture);
}
