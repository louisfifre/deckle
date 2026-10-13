namespace Deckle.Lighting.Ambient;

// Temporal smoothing of the tuned colour : one exponential moving
// average per key ("group", or a light id in multi mode), in float on
// the encoded channels. Damps small frame-to-frame jitter (moving
// highlights in a dark scene) without dulling real cuts.
//
// The stored SmoothingAlpha is the step for one 1/15 s tick ; the
// smoother applies it over the measured time since the previous tick
// (AmbientPushCadence.AdaptSmoothingAlpha), so the wall-clock response
// is the same at any push rate and across skipped ticks. A key seen for
// the first time, or reset, adopts its target (no fade-in from black).
//
// The state stays in [0, 1] and finite : a NaN channel in the target
// counts as 0 and a NaN alpha turns smoothing off, so one bad tick
// never poisons the trail.
//
// Not thread-safe : the push loop is its only caller.
internal sealed class AmbientColorSmoother
{
    private readonly Dictionary<string, SrgbColor> _states = new();

    public SrgbColor Smooth(string key, SrgbColor target, double referenceAlpha, double dtSeconds)
    {
        target = new SrgbColor(Sanitize(target.R), Sanitize(target.G), Sanitize(target.B));
        float alpha = (float)AmbientPushCadence.AdaptSmoothingAlpha(referenceAlpha, dtSeconds);
        SrgbColor next = _states.TryGetValue(key, out var state) && alpha < 1f
            ? new SrgbColor(
                alpha * target.R + (1f - alpha) * state.R,
                alpha * target.G + (1f - alpha) * state.G,
                alpha * target.B + (1f - alpha) * state.B)
            : target;
        _states[key] = next;
        return next;
    }

    public void Reset(string key) => _states.Remove(key);

    public void Clear() => _states.Clear();

    private static float Sanitize(float channel)
        => float.IsNaN(channel) ? 0f : Math.Clamp(channel, 0f, 1f);
}
