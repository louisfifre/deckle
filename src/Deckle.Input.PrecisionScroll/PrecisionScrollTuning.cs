namespace Deckle.Input.PrecisionScroll;

// The complete calibration surface for the gesture model. Each value expresses
// a distinct observable behaviour: distance, first-step pacing, or release timing.
public sealed record PrecisionScrollTuning
{
    public const double DistancePerDetentMinimum = 0.25;
    public const double DistancePerDetentMaximum = 6;
    public const double InitialStepDurationMinimum = 20;
    public const double InitialStepDurationMaximum = 180;
    public const double QuietPeriodScaleMinimum = 1;
    public const double QuietPeriodScaleMaximum = 4;

    public double DistancePerDetentMm { get; init; } = 1.5;
    public double InitialStepDurationMs { get; init; } = 60;
    public double QuietPeriodScale { get; init; } = 2;

    public PrecisionScrollTuning Normalize() => this with
    {
        DistancePerDetentMm = Math.Clamp(
            DistancePerDetentMm,
            DistancePerDetentMinimum,
            DistancePerDetentMaximum),
        InitialStepDurationMs = Math.Clamp(
            InitialStepDurationMs,
            InitialStepDurationMinimum,
            InitialStepDurationMaximum),
        QuietPeriodScale = Math.Clamp(
            QuietPeriodScale,
            QuietPeriodScaleMinimum,
            QuietPeriodScaleMaximum),
    };
}
