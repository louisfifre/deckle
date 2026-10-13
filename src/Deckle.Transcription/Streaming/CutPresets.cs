using Deckle.Presets;

namespace Deckle.Transcription;

// The built-in cut presets, named by effect: how patient the cut is with a
// pause.
//
// The shipped defaults ARE Balanced (read from the POCO initializers, never
// copied literals), so a fresh install shows a preset rather than Custom.
// Custom is a stored selection, not a derived one: see
// EnergySegmenterSettings.CutPreset for the one-time derivation.
public static class CutPresets
{
    public const string Patient = "patient";
    public const string Balanced = "balanced";
    public const string Quick = "quick";

    /// <summary>The preset a reset returns to: the shipped defaults.</summary>
    public const string Default = Balanced;

    public static readonly IReadOnlyList<Preset<CutCurveValues>> All =
    [
        new(Patient, "WhisperCutPresetPatient",
            new CutCurveValues(8000, 1000, 30000, 180000, 0.85, 0.10, 0.90, 0.25)),
        new(Balanced, "WhisperCutPresetBalanced",
            CutCurveValues.From(new EnergySegmenterSettings())),
        new(Quick, "WhisperCutPresetQuick",
            new CutCurveValues(2500, 500, 10000, 60000, 0.33, 0.33, 0.67, 0.67)),
    ];
}
