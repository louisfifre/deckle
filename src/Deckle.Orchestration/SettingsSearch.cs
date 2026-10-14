using Deckle.Catalog;

namespace Deckle.Orchestration;

// The page's contribution to the Settings search index: one entry per findable
// section. The list section is bespoke (its header is a TextBlock, not a
// SettingsCard), so its label is given inline and the same key is stamped as
// the section's Tag in XAML, the scroll-to handle. Keywords are the words a
// user brings that the label does not carry.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "OrchestrationPage_Section_Apps",
            LiteralLabel = "Your apps and games",
            Keywords =
            [
                "game", "steam", "application", "program", "executable", "exe",
                "hdr", "per-app", "profile",
            ],
        },
        // The scan section is bespoke like the list: inline label, same key as
        // its Tag. The folders entry below stays on the expander, a finer target
        // inside this section.
        new SettingSearchEntry
        {
            LabelKey = "OrchestrationPage_Section_Scan",
            LiteralLabel = "Scan",
            Keywords = ["steam", "library", "find", "discover", "import", "games", "apps"],
        },
        new SettingSearchEntry
        {
            LabelKey = "OrchestrationPage_FoldersExpander",
            LiteralLabel = "Folders to scan",
            Keywords = ["steam", "scan", "library", "folder"],
        },
    ];
}
