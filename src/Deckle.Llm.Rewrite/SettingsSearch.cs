using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── SettingsSearch — LlmPage's search contribution ────────────────────────────
//
// One SettingSearchEntry per findable card on the Rewriting page, flat and static
// so the shell's search index can pull it at boot without composing the page.
//
// Redeclared here rather than read off the composed manifests: the two composed
// leaves (LlmEnableCard, LlmEndpointExpander) live in instance properties of
// LlmGeneralViewModel, which owns live settings and cannot be built at boot
// without side effects. The index resolves display text from the module's PRI
// subtree by LabelKey alone, so the keys are all it needs.
//
// Coverage is hybrid, because the page is:
//   • composed leaves — LabelKey only; their runtime Tag is stamped by the
//     composer.
//   • bespoke cards — their Tag is authored by hand in the section XAML.
//   • runtime lists (profiles, models) — no per-item card to index; each is one
//     section-level entry with a LiteralLabel, targeting the Tag on its section
//     block.
//
// Maintenance contract: a card added to this page is a card added here. LabelKey
// must equal the card's Tag, the handle the post-navigation scroll-to looks for.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "LlmEnableCard",
            Keywords = ["ai", "rephrase", "reword", "polish"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LlmEndpointExpander",
            Keywords = ["server", "host", "api", "address"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmActiveProfileCard",
            Keywords = ["profile", "rewrite", "default"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmProfilesSection",
            LiteralLabel = "Profiles",
            Keywords = ["preset", "system prompt", "temperature", "persona"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmModelsSection",
            LiteralLabel = "Models",
            Keywords = ["ollama", "download", "install", "remove"],
        },
    ];
}
