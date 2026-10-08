using Deckle.Presets;

namespace Deckle.Transcription;

// The built-in cut presets, named by effect: how patient the cut is with a
// pause. The values are starting points, to be tuned by testing.
//
// The shipped defaults ARE Balanced (read from the POCO initializers, never
// copied literals), so a fresh install shows a preset rather than Custom. A
// customized stored curve matches nothing and reads as Custom, untouched:
// that is the intended migration (none).
public static class CutPresets
{
    public const string Patient = "patient";
    public const string Balanced = "balanced";
    public const string Quick = "quick";

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
