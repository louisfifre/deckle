using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The module's contribution to the shell's cross-page search index — one
// SettingSearchEntry per findable card, grouped by the page that holds it. The
// composition root reads these lists at boot and hands each to
// SettingsSearchIndex.Register alongside the matching nav descriptor; nothing here
// touches a running page. One list per page is a hard requirement, not a tidiness
// choice: the index stamps the page coordinates once per Register call, so cards
// pooled across pages would all navigate to the same one.
//
// Why the lists are declared here and not derived from a page's own manifest: the
// composable descriptors (AutocorrectSettingsManifest, DiagnosticsSettings) are
// INSTANCE properties of AutocorrectViewModel, and a ViewModel cannot be built at
// index time without the service wiring and side effects its construction pulls in.
// The index resolves display text from the module's PRI subtree by key, never by
// composing anything, so the searchable set is spelled out here as plain keys —
// mirroring the manifest's LabelKeys without instantiating it.
//
// Maintenance contract: one card added to a page is one entry added to that page's
// list. A card whose header resolves from a SettingsCard "<key>/Header" (every
// composed card, and the two navigation cards) needs only its LabelKey; a card
// whose label lives under a plain TextBlock "<key>.Text" has no "/Header" for the
// index to resolve, so it supplies LiteralLabel — and the SAME key must be stamped
// as the container's Tag in XAML, since LabelKey doubles as the scroll-to handle.
// Keywords are English, lowercase, matched but never shown; they exist only where a
// user would reach for a word the visible label does not already carry.
public static class SettingsSearch
{
    // ── AutocorrectPage ──────────────────────────────────────────────────────
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        // The master toggle. Its own label says "autocorrect"; the keywords reach it
        // from the vocabulary a user brings to the feature instead of its name.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_MasterCard",
            Keywords = ["spelling", "typo", "accents", "correction"],
        },

        // The two drill-in cards. Composed-shaped keys (.Header / .Description), so
        // the index resolves both without a literal. They are findable in their own
        // right: a user searching "apps" should land on the card that takes them
        // there, not only on the destination page's section.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_DomainsCard",
            Keywords = ["domain", "vocabulary", "dictionary", "lexicon", "jargon", "terms"],
        },
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_AppsCard",
            Keywords = ["application", "program", "per-app", "enrol", "enroll", "forget"],
        },

        // The exclusion register — on this page, not on a child, because an
        // exclusion holds against every domain and every app at once. Bespoke: a
        // live list with add and undo gestures, no composer and no "/Header" key,
        // its header a plain TextBlock under
        // "AutocorrectPage_Section_Exclusions.Text" which the index does not probe,
        // so the label is supplied inline and the same key is stamped as the
        // section's Tag in XAML. The keywords carry what a user reaches for at the
        // moment of annoyance — a word they want left alone — since none of those
        // words appear in the label.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_Section_Exclusions",
            LiteralLabel = "Excluded words",
            Keywords = ["exclude", "exception", "ignore", "never correct", "stop correcting", "word"],
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

    // ── LexicalDomainsPage ───────────────────────────────────────────────────
    public static IReadOnlyList<SettingSearchEntry> LexicalDomainsEntries { get; } =
    [
        // The domain tabs and their language rows. Bespoke — the tabs and rows are
        // enumerated at Load() with no composer and no "/Header" key — so the label
        // is supplied inline and the Tag is stamped on the section's container. The
        // keywords carry the model's own word ("domain") and the fields a user would
        // type looking for one, since a single visible label carries neither.
        //
        // The label names the section, not the page: "Lexical domains" is already
        // the drill-in card on AutocorrectPage, the rail entry and this page's own
        // title, so reusing it here would put two hits with the same words in the
        // list. This one says what the section holds — the domains and the
        // languages they are switched on in — and the breadcrumb beneath it says
        // which page that is.
        new SettingSearchEntry
        {
            LabelKey = "LexicalDomainsPage_Section_Domains",
            LiteralLabel = "Domains and languages",
            Keywords =
            [
                "domain", "dictionary", "computing", "jargon", "terms", "lexicon",
                "vocabulary", "language",
            ],
        },
    ];

    // ── AppsEnrolledPage ─────────────────────────────────────────────────────
    public static IReadOnlyList<SettingSearchEntry> AppsEnrolledEntries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "AppsEnrolledPage_Section_Apps",
            LiteralLabel = "Apps",
            Keywords = ["application", "program", "per-app", "enrol", "enroll", "forget"],
        },
    ];
}
