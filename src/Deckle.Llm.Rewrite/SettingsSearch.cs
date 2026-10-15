using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── SettingsSearch — LlmPage's search contribution ────────────────────────────
//
// One SettingSearchEntry per findable card on the Rewriting page, flat and static
// so the shell's search index can pull it at boot without composing the page.
//
// Redeclared here rather than read off the composed manifest: the composed
// leaf (LlmEnableCard) lives in an instance property of LlmGeneralViewModel,
// which owns live settings and cannot be built at boot without side effects.
// The index resolves display text from the module's PRI subtree by LabelKey
// alone, so the keys are all it needs.
//
// Coverage is hybrid, because the page is:
//   • a composed leaf — LabelKey only; its runtime Tag is stamped by the
//     composer.
//   • bespoke cards — their Tag is authored by hand in the section XAML.
//   • runtime lists (profiles, providers, models) — no per-item card to index;
//     each is one section-level entry with a LiteralLabel, targeting the Tag
//     on its section block.
//   • the "While you dictate" section — composed (LlmDictationViewModel), but
//     its batch-size card is hidden while the group is off, so the entry
//     targets the section block, always shown with the module, the same way.
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
            LabelKey = "LlmActiveProfileCard",
            Keywords = ["profile", "rewrite", "default"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmDictationSection",
            LiteralLabel = "While you dictate",
            Keywords = ["batch", "pause", "speak", "live", "stream"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmProfilesSection",
            LiteralLabel = "Profiles",
            Keywords = ["preset", "system prompt", "temperature", "persona"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmProvidersSection",
            LiteralLabel = "Providers",
            Keywords = ["server", "host", "api", "address", "key", "claude", "lm studio", "ollama"],
        },

        new SettingSearchEntry
        {
            LabelKey = "LlmModelsSection",
            LiteralLabel = "Models",
            Keywords = ["ollama", "download", "install", "remove"],
        },
    ];
}
