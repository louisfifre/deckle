using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Llm.Rewrite;

// ── SettingsSearch — LlmPage's search contribution ────────────────────────────
//
// One SettingSearchEntry per findable card on the Rewriting page, flat and static
// so the shell's search index can pull it at boot without composing the page.
//
// Why redeclared here rather than read off the composed manifest: the two composed
// leaves (LlmEnableCard, LlmEndpointExpander) live in GeneralSettingsManifest, an
// INSTANCE property on LlmGeneralViewModel — a live thing that owns the settings
// values and their service, not constructible at boot without side effects. The
// search index resolves display text from the module's PRI subtree by LabelKey
// alone, so it needs the KEYS, not the VM. This list hands over exactly those keys,
// declaratively, and never touches the ViewModel.
//
// Coverage is hybrid, because the page is:
//   • composed leaves — LabelKey only; the index resolves "<key>/Header" and
//     "/Description" from .resw. Their runtime Tag is stamped by the composer.
//   • bespoke cards — LabelKey resolves from .resw the same way; their Tag is
//     authored by hand in the section XAML (see the Tag="…" attributes).
//   • runtime lists (profiles, models) — no per-item card to
//     index; each is one section-level entry with a LiteralLabel taken from the
//     section header, targeting the section host element (Tag on its root panel).
//
// Maintenance contract: a card added to this page is a card added here. LabelKey
// must equal the card's Tag (the composer's stamp, or the hand-authored Tag) — that
// is the handle the post-navigation scroll-to walks the visual tree for.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // General — composed leaves. Tag stamped by the composer.
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

        // Shortcut slots — bespoke cards. Tag hand-authored in LlmShortcutSlotsSection.
        new SettingSearchEntry
        {
            LabelKey = "LlmPrimarySlotCard",
            Keywords = ["shortcut", "hotkey", "keybinding"],
        },
        new SettingSearchEntry
        {
            LabelKey = "LlmSecondarySlotCard",
            Keywords = ["shortcut", "hotkey", "keybinding"],
        },

        // Profiles — runtime ItemsRepeater, one section entry.
        new SettingSearchEntry
        {
            LabelKey = "LlmProfilesSection",
            LiteralLabel = "Profiles",
            Keywords = ["preset", "system prompt", "temperature", "persona"],
        },

        // Models — code-behind-generated cards, one section entry.
        new SettingSearchEntry
        {
            LabelKey = "LlmModelsSection",
            LiteralLabel = "Models",
            Keywords = ["ollama", "download", "install", "remove"],
        },
    ];
}
