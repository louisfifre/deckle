using Deckle.Anytype;
using Deckle.Catalog;
using Deckle.Modules;
using Deckle.Transcription.Whisper;

namespace Deckle.App;

// ── AppModules ────────────────────────────────────────────────────────────────
//
// The presence catalogue's content: the user-facing modules this build of
// Deckle carries, declared by the composition root. Descriptors live HERE and
// not in the modules they name (the deliberate inverse of the settings nav
// registry) because the installer companion's end state is a catalogue that can
// describe a module whose DLLs are not on disk yet — see Deckle.Modules.
//
// The id vocabulary lives in Deckle.Modules (ModuleIds) — the wizard's
// selector speaks it too. Support assemblies (Core, Audio, Vad, Vision,
// Lighting, …) are not modules — they are the floor the modules stand on and
// never appear here.
internal static class AppModules
{
    // Fills the catalogue. Called once from OnLaunched, before anything reads
    // ModulePresence, so the selector and the gates see the same set.
    public static void RegisterAll()
    {
        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = ModuleIds.Transcription,
            Glyph = Glyphs.Speech,
            Order = 100,
            // The engine ctor loads the model and faults without the native
            // runtime — the same probe that has always gated its composition.
            IsProvisioned = () => NativeRuntime.IsInstalled() && SpeechModels.IsDefaultInstalled(),
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
            Id = ModuleIds.Anytype,
            Glyph = Glyphs.List,
            Order = 600,
            IsProvisioned = BackendInstallation.IsInstalled,
        });
    }
}
