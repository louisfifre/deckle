namespace Deckle.Vision;

// Extension slot for scene-aware frame interpretation (saliency, day/night)
// beyond FrameSampler's plain average. It lives in Deckle.Vision, not in
// Deckle.Lighting.Ambient, so the consumer references the abstraction and
// analyzers stay out of the consumer's tree.

public interface IFrameAnalyzer
{
    /// <summary>Returns hints the consumer can use to bias its colour
    /// reduction, or <c>default</c> when the analyzer has nothing to say
    /// for this frame.</summary>
    FrameAnalysisHint Analyze(SampledFrame frame);
}
