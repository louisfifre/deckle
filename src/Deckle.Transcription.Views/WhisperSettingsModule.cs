using Deckle.Catalog;

namespace Deckle.Transcription.Views;

// ── WhisperSettingsModule ─────────────────────────────────────────────────────
//
// The transcription module's own nav identity for the Settings shell. The module
// declares its pages, their icons and their label keys here; the composition root
// supplies only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
//
// Three pages, one module: the module's own page, which the rail shows as
// General under the Transcription node (overlay, model and engine,
// diagnostics, the restart footer), then Decoding (the Whisper parameters that
// apply in both pipelines) and Streaming (the pipeline switch, speech
// filtering, segmentation) nested under it.
public static class WhisperSettingsModule
{
    // The PRI subtree of the nav labels and the search entries: the pages'
    // strings live with the pages, in the Views assembly.
    public const string ResourceLibrary = "Deckle.Transcription.Views";

    // The shell resolves these with Type.GetType, so they name the assembly
    // that compiles the pages.
    public const string PageTag = "Deckle.Transcription.Views.WhisperPage, Deckle.Transcription.Views";
    public const string DecodingPageTag = "Deckle.Transcription.Views.DecodingPage, Deckle.Transcription.Views";
    public const string StreamingPageTag = "Deckle.Transcription.Views.StreamingPage, Deckle.Transcription.Views";

    private const string ModuleId = "whisp";

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
    public static SettingsModuleDescriptor DescribeDecoding(int order) => new()
    {
        Id = "whisp-decoding",
        ParentId = ModuleId,
        PageTag = DecodingPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_Decoding",
        Glyph = Glyphs.Speech,
        Order = order,
    };

    public static SettingsModuleDescriptor DescribeStreaming(int order) => new()
    {
        Id = "whisp-streaming",
        ParentId = ModuleId,
        PageTag = StreamingPageTag,
        OwningAssembly = ResourceLibrary,
        LabelKey = "SettingsModuleNavLabel_Streaming",
        Glyph = Glyphs.Speech,
        Order = order,
    };
}
