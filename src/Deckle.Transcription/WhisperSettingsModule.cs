using Deckle.Catalog;

namespace Deckle.Transcription;

// ── WhisperSettingsModule ─────────────────────────────────────────────────────
//
// The transcription module's own nav identity for the Settings shell. The module
// declares its page, its icon and its label key here; the composition root supplies
// only the Order. Registered from App.OnLaunched via SettingsModuleRegistry.
public static class WhisperSettingsModule
{
    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "whisp",
        PageTag = "Deckle.Transcription.WhisperPage, Deckle.Transcription",
        OwningAssembly = "Deckle.Transcription",
        Glyph = Glyphs.Speech,
        Order = order,
    };
}
