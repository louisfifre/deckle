using System;

namespace Deckle.Audio;

// ── ListeningTake ──────────────────────────────────────────────────────────
//
// One recorded take for the Recording-page listening test, kept in memory, and
// the renders of it at a chosen pre-processing strength. Pure: no device, no
// thread, no log — ListeningTester records the take and plays a render.
//
// Reading of 'strength'. Strength is a dry/wet blend between the raw
// microphone signal and the fully processed one:
//     out[i] = raw[i] + (processed[i] - raw[i]) * strength
// so 0 is the raw take, 1 is exactly what dictation feeds the ASR backend, and
// 0.25 / 0.75 sit a quarter and three quarters of the way between. This is the
// lead's interpretation of Louis's "curseurs a 25 % et 75 %", not a parameter
// of the DSP itself. The blend is well defined because every stage of
// TranscriptionPreprocessor is sample-aligned (the limiter has no look-ahead):
// processed[i] is the treatment of raw[i], never of a shifted sample.
//
// The processed side uses the settings passed in whether their Enabled flag is
// on or off: TranscriptionPreprocessor.Process ignores Enabled (its callers
// gate on it), so "On" means "the current parameters applied", which is what a
// listener comparing Off and On wants to hear.
public sealed class ListeningTake
{
    // Takes ownership of `raw` (16 kHz mono float [-1, 1]); the caller hands
    // over a fresh capture buffer and does not keep writing to it.
    public ListeningTake(float[] raw)
    {
        Raw = raw ?? throw new ArgumentNullException(nameof(raw));
    }

    public float[] Raw { get; }

    // Always a new array; Raw is never mutated. Strength is clamped to [0, 1].
    public float[] Render(double strength, PreprocessingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        strength = Math.Clamp(strength, 0.0, 1.0);
        if (strength == 0.0 || Raw.Length == 0) return (float[])Raw.Clone();

        // Process clones its input, so Raw stays intact.
        float[] processed = TranscriptionPreprocessor.Process(Raw, settings).Pcm;
        if (strength == 1.0) return processed;

        float wet = (float)strength;
        var output = new float[Raw.Length];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = Raw[i] + (processed[i] - Raw[i]) * wet;
        }
        return output;
    }
}
