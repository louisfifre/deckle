using System;
using Deckle.Catalog;
using Deckle.Localization;

namespace Deckle.Autocorrect.Views;

// ── AutocorrectSettingsModule ─────────────────────────────────────────────────
//
// The autocorrect module's nav identity for the Settings shell: its pages, their
// icons and label keys. The composition root supplies only the Order.
//
// Three pages, one family: the Autocorrect page, plus Lexical domains and Apps
// enrolled nested under it in the rail. The page tags are constants because
// AutocorrectPage's navigation cards also drill in to them.
public static class AutocorrectSettingsModule
{
    // This module's PRI subtree. Code resolving a module string through
    // Loc.GetFrom reads it from here, never from a repeated literal.
    public const string ResourceLibrary = "Deckle.Autocorrect.Views";

    public const string PageTag = "Deckle.Autocorrect.Views.AutocorrectPage, Deckle.Autocorrect.Views";

    public const string LexicalDomainsPageTag =
        "Deckle.Autocorrect.Views.LexicalDomainsPage, Deckle.Autocorrect.Views";

    public const string AppsEnrolledPageTag =
        "Deckle.Autocorrect.Views.AppsEnrolledPage, Deckle.Autocorrect.Views";

    private const string ModuleId = "autocorrect";

    // The Lexicon page of the transcription module (the Dictation node), where
    // the person's words are kept. A literal, as the page tag is the same
    // Type.GetType string its rail entry carries: this module does not
    // reference that one.
    public const string TranscriptionLexiconPageTag =
        "Deckle.Transcription.Views.LexiconPage, Deckle.Transcription.Views";

    // Whether the transcription module, which holds the person's lexicon, is
    // present, so the page can lead to it from the "Lexicon" card. That
    // module is one this one does not reference; the App, which knows both,
    // sets it. Unset, the page says nothing about the lexicon.
    public static bool TranscriptionLexiconPresent { get; set; }

    // ── Paragraph correction ─────────────────────────────────────────────────
    //
    // The correction itself runs in the Rewrite module, which this one does not
    // reference; the App, which knows both, sets what the page needs. Unset (no
    // Rewrite module), the page has no Paragraph correction section.
    public static bool ParagraphCorrectionPresent { get; set; }

    // What paragraph correction lacks on this PC, so the bar says what to set
    // up. Read when the page shows and again after each
    // ParagraphCorrectionSetupChanged.
    public static Func<ParagraphCorrectionSetup>? ParagraphCorrectionSetup { get; set; }

    // The model the bar names, taken from the Rewrite module's pinned model.
    public static string ParagraphCorrectionModel { get; set; } = "";

    // Where "Open Rewriting" leads: the Rewriting page's tag.
    public static string? ParagraphCorrectionSetupPageTag { get; set; }

    // Raised by the App when the answer of ParagraphCorrectionNeedsSetup may
    // have changed. On any thread: the page marshals it.
    public static event Action? ParagraphCorrectionSetupChanged;

    public static void RaiseParagraphCorrectionSetupChanged() =>
        ParagraphCorrectionSetupChanged?.Invoke();

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = ModuleId,
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };

    // The children carry a Glyph like any module because the search index shows one
    // beside a hit; the rail deliberately renders them without it (the parent's icon
    // names the family, indentation names the rest).
    public static SettingsModuleDescriptor DescribeLexicalDomains(int order) => new()
    {
        Id = "autocorrect-domains",
        ParentId = ModuleId,
        PageTag = LexicalDomainsPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_LexicalDomains",
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };

    public static SettingsModuleDescriptor DescribeAppsEnrolled(int order) => new()
    {
        Id = "autocorrect-apps",
        ParentId = ModuleId,
        PageTag = AppsEnrolledPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_AppsEnrolled",
        Glyph = Glyphs.Autocorrect,
        Order = order,
    };
}
