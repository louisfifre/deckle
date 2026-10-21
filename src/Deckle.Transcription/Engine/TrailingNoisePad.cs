namespace Deckle.Transcription;

// Appends a short tail of very quiet noise to an utterance before it reaches
// the backend.
//
// Why: after the VAD trim an utterance ends 30 ms after the last speech, so the
// decoder sees near-digital silence right behind the final word. Replayed on
// the same captured audio, Whisper then sometimes emitted end-of-text early and
// dropped the last words (3 full endings out of 8). Appending 500 ms of exact
// digital silence did not help (0 out of 8); appending 500 ms of noise at about
// -60 dBFS RMS did (8 out of 8). So the pad must be noise, not zeros. Whether
// the model reads digital silence as "the recording is over" is an inference
// from that replay, not something established inside Whisper.
//
// The noise comes from a fixed-seed generator, so one input always yields the
// same output: a replayed take stays reproducible.
internal static class TrailingNoisePad
{
    internal const int SampleRate = 16_000;
    internal const int PadMs = 500;
    internal const int PadSamples = SampleRate / 1000 * PadMs;
    internal const double TargetRmsDbfs = -60.0;

    private const int Seed = 0x5EED;

    // A uniform variable on [-a, a] has an RMS of a / sqrt(3), so a is the
    // target RMS times sqrt(3). Uniform, not gaussian: bounded, and no tail
    // sample can exceed the target peak.
    private static readonly float Amplitude =
        (float)(Math.Pow(10.0, TargetRmsDbfs / 20.0) * Math.Sqrt(3.0));

    // Returns a new buffer: the samples followed by the pad. The input is not
    // modified, since the caller keeps it as the corpus record (BackendAudio).
    internal static float[] Append(ReadOnlySpan<float> samples)
    {
        var padded = new float[samples.Length + PadSamples];
        samples.CopyTo(padded);

        var random = new Random(Seed);
        for (int i = samples.Length; i < padded.Length; i++)
            padded[i] = (float)(random.NextDouble() * 2.0 - 1.0) * Amplitude;

        return padded;
    }
}
