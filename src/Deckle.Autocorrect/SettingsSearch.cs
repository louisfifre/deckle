using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// AutocorrectPage's contribution to the shell's cross-page search index — one
// SettingSearchEntry per findable card on the page. The composition root reads this
// list at boot and hands it to SettingsSearchIndex.Register alongside the module's
// nav descriptor; nothing here touches the running page.
//
// Why the list is declared here and not derived from the page's own manifest: the
// composable descriptors (AutocorrectSettingsManifest, DiagnosticsSettings) are
// INSTANCE properties of AutocorrectViewModel, and a ViewModel cannot be built at
// index time without the service wiring and side effects its construction pulls in.
// The index resolves display text from the module's PRI subtree by key, never by
// composing anything, so the searchable set is spelled out here as plain keys —
// mirroring the manifest's LabelKeys without instantiating it.
//
// Maintenance contract: one card added to the page is one entry added here. A card
// whose header resolves from a SettingsCard "<key>.Header"/.Description (every
// composed card) needs only its LabelKey; a card whose label lives under a plain
// TextBlock "<key>.Text" — the Apps section header below — has no "/Header" for the
// index to resolve, so it supplies LiteralLabel. Keywords are English, lowercase,
// matched but never shown; they exist only where a user would reach for a word the
// visible label does not already carry.
public static class SettingsSearch
{
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // The master toggle. Its own label says "autocorrect"; the keywords reach it
        // from the vocabulary a user brings to the feature instead of its name.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_MasterCard",
            Keywords = ["spelling", "typo", "accents", "correction"],
        },

        // The per-app section. Bespoke, runtime-enumerated rows with no composer and
        // no "/Header" key — its header is a plain TextBlock resolving under
        // "AutocorrectPage_Section_Apps.Text", which the index does not probe — so the
        // label is supplied inline. The matching Tag is stamped on the section's
        // container in the page XAML, the handle the scroll-to walks the tree for.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_Section_Apps",
            LiteralLabel = "Apps",
            Keywords = ["application", "program", "per-app", "forget"],
        },

        // The vocabulary-pack section. Bespoke like the Apps one, and for the same
        // reason its label is supplied inline: the header is a plain TextBlock under
        // "AutocorrectPage_Section_Packs.Text", which the index does not probe. The
        // keywords carry the model's own word ("domain") and what a user would type
        // looking for it, since the visible label says neither.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_Section_Packs",
            LiteralLabel = "Vocabulary packs",
            Keywords = ["domain", "pack", "dictionary", "computing", "jargon", "terms", "lexicon"],
        },

        // Diagnostics — the two purpose-specific dataset toggles. Each resolves from
        // its SettingsCard header; the keywords carry the words ("diagnostics",
        // "telemetry", "corpus") a user searches for the category rather than the
        // individual toggle wording.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectDecisionsCard",
            Keywords = ["diagnostics", "telemetry", "logging"],
        },
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectTextCard",
            Keywords = ["corpus", "telemetry", "diagnostics", "keystrokes"],
        },
    ];
}
