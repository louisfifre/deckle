namespace Deckle.Transcription;

// RMS and peak of a float PCM buffer in dBFS, for the log. -120 stands for
// silence or an empty buffer, the floor the heartbeat uses too.
internal static class SampleLevels
{
    internal const double SilenceDbfs = -120.0;

    internal static (double RmsDbfs, double PeakDbfs) Measure(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return (SilenceDbfs, SilenceDbfs);

        double sumSquares = 0;
        float peak = 0f;
        foreach (float s in samples)
        {
            sumSquares += (double)s * s;
            float magnitude = Math.Abs(s);
            if (magnitude > peak) peak = magnitude;
        }

        double rms = Math.Sqrt(sumSquares / samples.Length);
        return (ToDbfs(rms), ToDbfs(peak));
    }

    private static double ToDbfs(double linear) =>
        linear > 1e-6 ? 20.0 * Math.Log10(linear) : SilenceDbfs;
}
