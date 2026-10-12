using Deckle.Presets;

namespace Deckle.Lighting.Ambient;

// The built-in Ambient presets, one line each. Ids are stored in
// settings.json (AmbientSettings.Preset), so they never change once shipped;
// the labels resolve from this module's .resw as "<LabelKey>.Content".
//
// The values are starting points. Louis (2026-10-12): Game at saturation
// 1.0 and exposure 0, "a low threshold and a really quite high reaction
// speed" (no figures yet, so Game keeps 6 and 0.40); Vivid is Game with
// saturation 1.10, made for Hades; Movie is to be settled before Ambient.
// He tunes in front of the scene and the figures land here, one edit per
// preset.
//
// Ambient is the default and reads the POCO initializers, so a fresh
// install, the per-card reset and the page reset agree on one source.
public static class AmbientPresets
{
    public const string Game = "game";
    public const string Vivid = "vivid";
    public const string Movie = "movie";
    public const string Ambient = "ambient";

    public const string Default = Ambient;

    public static readonly IReadOnlyList<Preset<AmbientTuning>> All =
    [
        new(Game, "AmbientPresetGame",
            new AmbientTuning(0.0, 1.0, 0.33, 0.33, 0.67, 0.67, 0.40, 6)),
        new(Vivid, "AmbientPresetVivid",
            new AmbientTuning(0.0, 1.10, 0.33, 0.33, 0.67, 0.67, 0.40, 6)),
        new(Movie, "AmbientPresetMovie",
            new AmbientTuning(0.0, 0.9, 0.42, 0.08, 0.58, 0.92, 0.15, 8)),
        new(Ambient, "AmbientPresetAmbient",
            AmbientTuning.From(new AmbientSettings())),
    ];
}
