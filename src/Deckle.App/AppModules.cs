using Deckle.Catalog;
using Deckle.Modules;

namespace Deckle.App;

// Descriptors live here, not in the modules they name, so the catalogue can
// describe a module whose DLLs are not on disk. Support assemblies (Core, Audio,
// Vision, Lighting...) are not modules and never appear here.
internal static class AppModules
{
    // Before anything reads ModulePresence, so the selector and the gates see
    // the same set.
    public static void RegisterAll()
    {
        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Transcription,
            Glyph = Glyphs.Speech,
            Order = 100,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Rewrite,
            Glyph = Glyphs.Sparkle,
            Order = 200,
            // The rewrite stage runs inside the transcription pipeline; alone
            // it has nothing to rewrite.
            DependsOn = [ModuleIds.Transcription],
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Autocorrect,
            Glyph = Glyphs.Autocorrect,
            Order = 300,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Ambient,
            Glyph = Glyphs.Lightbulb,
            Order = 400,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Trackpad,
            Glyph = Glyphs.Trackpad,
            Order = 500,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.PrecisionScroll,
            Glyph = Glyphs.Mouse,
            Order = 550,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Anytype,
            Glyph = Glyphs.List,
            Order = 600,
        });
    }
}
