using System.Collections.Generic;
using Deckle.Catalog;

namespace Deckle.Autocorrect.Views;

// ── SettingsSearch ────────────────────────────────────────────────────────────
//
// The module's contribution to the shell's cross-page search index: one
// SettingSearchEntry per findable card, in one list per page. The split is a hard
// requirement: the index stamps the page coordinates once per Register call, so
// cards pooled across pages would all navigate to the same one.
//
// The lists are spelled out as plain keys rather than derived from the page
// manifests, because those are instance properties of AutocorrectViewModel, whose
// construction loads the module's settings. One card added to a page is one entry
// added to that page's list.
public static class SettingsSearch
{
    // ── AutocorrectPage ──────────────────────────────────────────────────────
    //
    // A getter, not a list built once: two cards exist only when the App has
    // said the module they lead to is present (the flags of
    // AutocorrectSettingsModule), and the App sets them before it registers.
    public static IReadOnlyList<SettingSearchEntry> Entries
    {
        get
        {
            var entries = new List<SettingSearchEntry>(PageEntries);
            if (AutocorrectSettingsModule.ParagraphCorrectionPresent)
                entries.Insert(1, ParagraphEntry);
            if (AutocorrectSettingsModule.TranscriptionLexiconPresent)
                entries.Add(YourWordsEntry);
            return entries;
        }
    }

    // The two bespoke labels are supplied inline, like the exclusion register's:
    // the card is a hand-placed one whose hit must read the same without the
    // page ever being composed.
    private static readonly SettingSearchEntry ParagraphEntry = new()
    {
        LabelKey = "AutocorrectPage_ParagraphCard",
        LiteralLabel = "Correct a paragraph with Shift+Enter",
        Keywords = ["paragraph", "shift+enter", "ollama", "correction", "correct", "rewrite", "local"],
    };

    private static readonly SettingSearchEntry YourWordsEntry = new()
    {
        LabelKey = "AutocorrectPage_DeclaredWordsCard",
        LiteralLabel = "Lexicon",
        Keywords = ["lexicon", "dictation", "keep", "vocabulary", "declared"],
    };

    private static readonly SettingSearchEntry[] PageEntries =
    [
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_MasterCard",
            Keywords = ["spelling", "typo", "accents", "correction"],
        },

        // The exclusion register sits on this page, not on a child, because an
        // exclusion holds against every domain and every app at once. Its title is
        // a SettingsSection title the index does not probe, so the label is
        // supplied inline.
        new SettingSearchEntry
        {
            LabelKey = "AutocorrectPage_Section_Exclusions",
            LiteralLabel = "Excluded words",
            Keywords = ["exclude", "exception", "ignore", "never correct", "stop correcting", "word"],
        },
    ];

    // ── LexicalDomainsPage ───────────────────────────────────────────────────
    public static IReadOnlyList<SettingSearchEntry> LexicalDomainsEntries { get; } =
    [
        // The label names the section, not the page: "Lexical domains" is already
        // the rail entry and this page's own title, so reusing it here would put
        // two hits with the same words in the list.
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
