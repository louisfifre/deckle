using Deckle.Catalog;

namespace Deckle.Audio;

// ── RecordingSettingsModule ───────────────────────────────────────────────────
//
// The audio-capture module's nav identity for the Settings shell. Recording used
// to be a static anchor in SettingsWindow.xaml; the Move H relocation makes it a
// registered module page like the others, so the module owns its page, icon and
// label key here and the composition root supplies only the Order. Order 50 keeps
// it first in the module band — right after General, before the other modules —
// exactly where the static anchor sat.
public static class RecordingSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "recording",
        PageTag = "Deckle.Audio.RecordingPage, Deckle.Audio",
        OwningAssembly = "Deckle.Audio",
        Glyph = Glyphs.Microphone,
        Order = order,
    };
}
