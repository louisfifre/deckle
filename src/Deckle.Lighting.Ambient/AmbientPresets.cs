using Deckle.Presets;

namespace Deckle.Lighting.Ambient;

// The built-in Ambient presets, one line each: two axes, the rendering
// (Vivid or Realistic) and the speed (Fast or Slow), then Custom in the
// selector (Louis, 2026-10-13: Vivid Fast, Vivid Slow, Realistic Fast,
// Realistic Slow, Custom). Ids are stored in settings.json
// (AmbientSettings.Preset), so they never change once shipped; the labels
// resolve from this module's .resw as "<LabelKey>.Content".
//
// Speed is the pair (SmoothingAlpha, ChangeThreshold): Fast is the former
// Game pair (0.40, 6), Slow the former Movie pair (0.15, 8). Rendering is the
// pair (ExposureEv, SaturationBoost): Realistic 0 EV and 1.0, Vivid 0 EV and
// 1.10. The curve is the same four-point curve in all of them, the former
// Game curve (a plain, near-linear response): Louis fixed no curve per axis.
// ALL FOUR VALUE PAIRS AND THE CURVE ARE PROVISIONAL until Louis has tuned
// them in front of the scene.
//
// Realistic Slow is the default and reads the POCO initializers, so a fresh
// install, the per-card reset and the page reset agree on one source.
public static class AmbientPresets
{
    public const string VividFast = "vivid-fast";
    public const string VividSlow = "vivid-slow";
    public const string RealisticFast = "realistic-fast";
    public const string RealisticSlow = "realistic-slow";

    public const string Default = RealisticSlow;

    // Fast and Slow speed pairs; Realistic and Vivid saturation.
    private const double FastAlpha = 0.40;
    private const int FastThreshold = 6;
    private const double SlowAlpha = 0.15;
    private const int SlowThreshold = 8;
    private const double RealisticSaturation = 1.0;
    private const double VividSaturation = 1.10;

    public static readonly IReadOnlyList<Preset<AmbientTuning>> All =
    [
        new(VividFast, "AmbientPresetVividFast",
            new AmbientTuning(0.0, VividSaturation, 0.33, 0.33, 0.67, 0.67, FastAlpha, FastThreshold)),
        new(VividSlow, "AmbientPresetVividSlow",
            new AmbientTuning(0.0, VividSaturation, 0.33, 0.33, 0.67, 0.67, SlowAlpha, SlowThreshold)),
        new(RealisticFast, "AmbientPresetRealisticFast",
            new AmbientTuning(0.0, RealisticSaturation, 0.33, 0.33, 0.67, 0.67, FastAlpha, FastThreshold)),
        new(RealisticSlow, "AmbientPresetRealisticSlow",
            AmbientTuning.From(new AmbientSettings())),
    ];

    // The ids of the presets before the two-axis recomposition, and the
    // preset that replaces each. A file that stored one of them follows the
    // replacement, values included (see MigrateRetiredSelection).
    private static readonly IReadOnlyDictionary<string, string> RetiredIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["game"]    = RealisticFast,
            ["movie"]   = RealisticSlow,
            ["vivid"]   = VividFast,
            ["ambient"] = RealisticSlow,
        };

    /// <summary>The preset that replaces a retired id, or null when the id
    /// is not a retired one.</summary>
    public static string? ReplacementOf(string? id)
        => id is not null && RetiredIds.TryGetValue(id, out var replacement) ? replacement : null;

    /// <summary>When the stored selection is a retired id, selects its
    /// replacement and writes the replacement's values: the person was on a
    /// preset, so they follow it. Returns the retired id that was replaced,
    /// or null when nothing changed. Any other selection, an unknown one
    /// included, is left to the preset family (it derives Custom from the
    /// values).</summary>
    public static string? MigrateRetiredSelection(AmbientSettings settings)
    {
        string? asked = settings.Preset;
        string? replacement = ReplacementOf(asked);
        if (replacement is null) return null;

        var preset = All.Single(p => p.Id == replacement);
        settings.Preset = replacement;
        preset.Values.ApplyTo(settings);
        return asked;
    }
}
