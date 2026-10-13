using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The module's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card, in one list per page. The split is a hard
// requirement: the index stamps the page coordinates once per Register call, so
// cards pooled across pages would all navigate to the same one.
//
// The lists are spelled out as plain keys rather than derived from the page
// manifests, because those are instance properties of AutocorrectViewModel, which
// cannot be built at index time without the service wiring its construction pulls in.
//
// Maintenance contract: one card added to a page is one entry added to that page's
// list. A card whose header resolves from a SettingsCard "<key>/Header" needs only
// its LabelKey; a card whose label lives under a plain TextBlock "<key>.Text" has
// no "/Header" for the index to resolve, so it supplies LiteralLabel, and the SAME
// key must be stamped as the container's Tag in XAML, since LabelKey doubles as the
// scroll-to handle. Keywords are English, lowercase, matched but never shown; they
// exist only where a user would reach for a word the visible label does not carry.
public static class SettingsSearch
{
    // ── AutocorrectPage ──────────────────────────────────────────────────────
    public static IReadOnlyList<SettingSearchEntry> Entries { get; } =
    [
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_MasterCard",
            Keywords = ["spelling", "typo", "accents", "correction"],
        },

        // The drill-in cards are findable in their own right: a search for "apps"
        // should land on the card that leads there, not only on the destination page.
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

        // The exclusion register sits on this page, not on a child, because an
        // exclusion holds against every domain and every app at once. Its header is
        // a plain TextBlock, so the label is supplied inline.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_Section_Exclusions",
            LiteralLabel = "Excluded words",
            Keywords = ["exclude", "exception", "ignore", "never correct", "stop correcting", "word"],
        },

        // Diagnostics: the two opt-in dataset toggles.
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
        // The label names the section, not the page: "Lexical domains" is already
        // the drill-in card on AutocorrectPage, the rail entry and this page's own
        // title, so reusing it here would put two hits with the same words in the
        // list.
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
