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
    ];
}
