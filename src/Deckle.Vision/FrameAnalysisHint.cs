namespace Deckle.Vision;

// Optional hints an IFrameAnalyzer can publish about a SampledFrame. Every
// field is nullable: an analyzer fills only what it computes, and a consumer
// reads null as "no opinion".

public readonly record struct FrameAnalysisHint
{
    /// <summary>Mean luminance of the frame in [0, 1].</summary>
    public double? MeanLuma { get; init; }

    /// <summary>Position of the brightest cluster in [0, 1]² grid
    /// space.</summary>
    public (double X, double Y)? PeakPosition { get; init; }

    /// <summary>Day / night estimate in [-1, 1]: +1 is daylight, -1 is
    /// night.</summary>
    public double? DayNightEstimate { get; init; }
}
