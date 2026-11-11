using Deckle.Catalog;

namespace Deckle.Llm.Rewrite.Views;

public static class LlmSettingsModule
{
    // Also the parent a profile's subpage names in its trail. The shell
    // resolves it with Type.GetType, so it names the assembly that compiles
    // the page.
    public const string PageTag = "Deckle.Llm.Rewrite.Views.LlmPage, Deckle.Llm.Rewrite.Views";

    // Whether transcription streams, read by the Dictation section: without
    // it no passage exists before Stop, so the steps' Streaming switch is
    // hidden and every step rewrites at Stop. The transcription settings live
    // in a module this one does not reference, so the host that knows both
    // sets it; unset, the section takes streaming as on.
    public static Func<bool>? TranscriptionStreams { get; set; }

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "llm",
        PageTag = PageTag,
        // The PRI subtree of the nav label and the search entries: the page's
        // strings live with the page, in the Views assembly.
        OwningAssembly = "Deckle.Llm.Rewrite.Views",
        Glyph = Glyphs.Sparkle,
        Order = order,
    };
}
