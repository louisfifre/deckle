using Deckle.Catalog;

namespace Deckle.Audio.Views;

// ── RecordingSettingsModule ───────────────────────────────────────────────────
//
// The audio-capture module's nav identity for the Settings shell: the
// Microphone page, nested under the Dictation node. The module owns its page,
// icon and label key here; the composition root supplies only the Order.
public static class RecordingSettingsModule
{
    // The Dictation node's module id, spelled out: this module cannot reference
    // Deckle.Transcription.Views, so the two meet on the string, the way the
    // taskbar cover meets the Taskbar. A ParentId naming no registered module
    // leaves the page out of the rail.
    internal const string DictationModuleId = "whisp";

    // The shell resolves it with Type.GetType, so it names the assembly that
    // compiles the page.
    public const string PageTag = "Deckle.Audio.Views.RecordingPage, Deckle.Audio.Views";

    public static SettingsModuleDescriptor Describe(int order) => new()
    {
        Id = "recording",
        ParentId = DictationModuleId,
        PageTag = PageTag,
        // The PRI subtree of the nav label and the search entries: the page's
        // strings live with the page, in the Views assembly.
        OwningAssembly = "Deckle.Audio.Views",
        Glyph = Glyphs.Microphone,
        Order = order,
    };
}
