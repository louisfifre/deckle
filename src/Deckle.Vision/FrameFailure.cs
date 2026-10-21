namespace Deckle.Vision;

// Last cause of a dropped frame. The consumer aggregates per-frame failures
// into one incident and needs the cause for its own milestone, while the
// per-frame details stay Verbose and governed. Stage is texture_query,
// frame_consumer, sampler_map or sampler_process; Hr is 0 and the exception
// fields empty when the stage threw nothing.
public sealed record FrameFailure(string Stage, int Hr, string ExType, string ExMessage);
