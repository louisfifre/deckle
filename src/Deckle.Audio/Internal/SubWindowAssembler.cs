namespace Deckle.Audio.Internal;

// Cuts the 16 kHz mono stream into the 50 ms sub-windows that the segmenter,
// the low-audio warning, the telemetry and the HUD all read: 800 samples, one
// linear RMS each. The cut is continuous across packets, so the window length
// does not depend on how the device chunks its audio (a 10 ms period gives
// five packets per window, a 3 ms period seventeen).
//
// A partial window left at the end of a take yields nothing, so every window the
// consumers read is a full 50 ms; its samples still reach the take's audio.
internal sealed class SubWindowAssembler
{
    public const int SampleRate = 16000;
    public const int WindowMs = 50;
    public const int WindowSamples = SampleRate * WindowMs / 1000; // 800

    public delegate void WindowHandler(ReadOnlySpan<float> window, float rms);

    private readonly float[] _pending = new float[WindowSamples];
    private int _pendingCount;

    public void Push(ReadOnlySpan<float> samples, WindowHandler onWindow)
    {
        while (!samples.IsEmpty)
        {
            // A window is completed from the carried samples plus the head of
            // the new ones; a window wholly inside the new samples needs no copy.
            if (_pendingCount == 0 && samples.Length >= WindowSamples)
            {
                Emit(samples[..WindowSamples], onWindow);
                samples = samples[WindowSamples..];
                continue;
            }

            int take = Math.Min(WindowSamples - _pendingCount, samples.Length);
            samples[..take].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += take;
            samples = samples[take..];

            if (_pendingCount == WindowSamples)
            {
                Emit(_pending, onWindow);
                _pendingCount = 0;
            }
        }
    }

    // Linear RMS [0, 1], clamped so a rare overshoot never escapes the range.
    private static void Emit(ReadOnlySpan<float> window, WindowHandler onWindow)
    {
        double sumSquares = 0;
        foreach (float sample in window)
            sumSquares += (double)sample * sample;
        float rms = (float)Math.Min(1.0, Math.Sqrt(sumSquares / window.Length));
        onWindow(window, rms);
    }
}
