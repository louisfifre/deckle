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
// The ids below are the vocabulary of the presence model: the presence file
// stores them, the selector's wording is keyed by them, and OnLaunched gates
// each module's composition and settings registration on them. Support
// assemblies (Core, Audio, Vad, Vision, Lighting, …) are not modules — they are
// the floor the modules stand on and never appear here.
internal static class AppModules
{
    public const string Transcription = "transcription";
    public const string Rewrite       = "rewrite";
    public const string Autocorrect   = "autocorrect";
    public const string Ambient       = "ambient";
    public const string Trackpad      = "trackpad";
    public const string Anytype       = "anytype";

    // Fills the catalogue. Called once from OnLaunched, before anything reads
    // ModulePresence, so the selector and the gates see the same set.
    public static void RegisterAll()
    {
        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Transcription,
            Glyph = Glyphs.Speech,
            Order = 100,
            // The engine ctor loads the model and faults without the native
            // runtime — the same probe that has always gated its composition.
            IsProvisioned = () => NativeRuntime.IsInstalled() && SpeechModels.IsDefaultInstalled(),
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Rewrite,
            Glyph = Glyphs.Sparkle,
            Order = 200,
            // The rewrite stage runs inside the transcription pipeline; alone
            // it has nothing to rewrite.
            DependsOn = [Transcription],
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Autocorrect,
            Glyph = Glyphs.Autocorrect,
            Order = 300,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Ambient,
            Glyph = Glyphs.Lightbulb,
            Order = 400,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Trackpad,
            Glyph = Glyphs.Trackpad,
            Order = 500,
        });

        ModuleRegistry.Register(new ModuleDescriptor
        {
            Id = Anytype,
            Glyph = Glyphs.List,
            Order = 600,
            IsProvisioned = BackendInstallation.IsInstalled,
        });
    }
}
