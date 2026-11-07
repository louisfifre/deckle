namespace Deckle.Audio;

// The level of what one WASAPI packet carried, as 16 kHz mono float [-1, 1]:
// the fast side of the capture, for an instrument that has to follow the voice
// as it happens. A packet is the device period long (10 ms on most
// microphones, as little as 3 ms where the driver allows it), so a level
// arrives within one period of the sound instead of after the 50 ms frame that
// the sub-window assembler needs to fill.
//
// Rms and Peak are linear [0, 1], of the packet's own samples. DurationMs is
// the audio the packet covers, so a consumer can smooth in time rather than
// per call. The 50 ms AudioLevel and Frame events stay the signal the
// segmenter, the low-audio warning and the telemetry read; this one only
// drives what a person watches.
public readonly record struct CaptureLevel(float Rms, float Peak, double DurationMs)
{
    // The level of a block of samples; empty gives zero.
    public static CaptureLevel Of(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty) return new CaptureLevel(0f, 0f, 0);

        double sumSquares = 0;
        float peak = 0f;
        foreach (float sample in samples)
        {
            sumSquares += (double)sample * sample;
            float magnitude = Math.Abs(sample);
            if (magnitude > peak) peak = magnitude;
        }

        float rms = (float)Math.Min(1.0, Math.Sqrt(sumSquares / samples.Length));
        return new CaptureLevel(rms, Math.Min(1f, peak), samples.Length * 1000.0 / sampleRate);
    }
}
