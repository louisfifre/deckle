using Deckle.Catalog;

namespace Deckle.Audio;

// ── RecordingSettingsModule ───────────────────────────────────────────────────
//
// The audio-capture module's nav identity for the Settings shell. The module
// owns its page, icon and label key here; the composition root supplies only the
// Order.
public static class RecordingSettingsModule
{
    public const string PageTag = "Deckle.Audio.RecordingPage, Deckle.Audio";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "recording",
        PageTag = PageTag,
        OwningAssembly = "Deckle.Audio",
        Glyph = Glyphs.Microphone,
        Order = order,
    };
}
