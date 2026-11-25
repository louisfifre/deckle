using Deckle.Catalog;

namespace Deckle.Transcription.Views;

// ── TranscriptionSettingsModule ─────────────────────────────────────────────────────
//
// The transcription module's own nav identity for the Settings shell. The module
// declares its pages, their icons and their label keys here; the composition
// root supplies only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
//
// The rail shows the module as the Dictation node. Its pages: the module's own
// page, which the rail shows as General under the node (the streaming switch,
// the overlay and the recording length, then the engine and models folder and,
// in developer mode, the segmentation; the restart footer), then Microphone (the
// audio module's page, which nests itself under this node on the module id
// below) and Lexicon (the person's own words).
public static class TranscriptionSettingsModule
{
    // The PRI subtree of the nav labels and the search entries: the pages'
    // strings live with the pages, in the Views assembly.
    public const string ResourceLibrary = "Deckle.Transcription.Views";

    // The shell resolves these with Type.GetType, so they name the assembly
    // that compiles the pages.
    public const string PageTag = "Deckle.Transcription.Views.TranscriptionPage, Deckle.Transcription.Views";
    public const string LexiconPageTag = "Deckle.Transcription.Views.LexiconPage, Deckle.Transcription.Views";

    // The node's id, public so the Microphone page of the audio module, which
    // cannot reference this assembly, names it on a string of its own.
    public const string ModuleId = "whisp";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = ModuleId,
        PageTag = PageTag,
        OwningAssembly = ResourceLibrary,
        Glyph = Glyphs.Speech,
        Order = order,
    };

    // The children carry a Glyph like any module because the search index shows one
    // beside a hit; the rail renders them without it (the node's icon names the
    // module, indentation names the rest).
    public static SettingsModuleDescriptor DescribeLexicon(int order) => new()
    {
        Id = "whisp-lexicon",
        ParentId = ModuleId,
        PageTag = LexiconPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_Lexicon",
        Glyph = Glyphs.Speech,
        Order = order,
    };
}
