namespace Deckle.Audio.Internal;

// Interleaved device frames to one mono float stream, in the device's own
// sample rate. The first of the two steps that turn the device format into the
// 16 kHz mono float every consumer expects (the second is StreamingResampler).
//
// A microphone with several channels (a stereo headset, a mic array exposed in
// raw mode) becomes the arithmetic mean of its channels, in channel order:
// plain, deterministic, and identical on every machine, which is the point of
// doing it here. PortAudio's own mono mixer is not used because it keeps the
// left channel only (pa_win_wasapi.c, _WASAPI_MONO_TO_STEREO_MIXER_2_TO_1_L),
// which silences a microphone wired to the right one.
//
// A sample that is not a finite number (NaN or infinity from a faulty driver)
// is read as silence: one such value would otherwise turn every RMS that
// includes it into NaN, and the meters, the segmenter and the telemetry after
// it.
internal static class MonoConverter
{
    // Writes frames mono samples from frames * channels interleaved ones. The
    // destination must be at least frames long.
    public static void ToMono(ReadOnlySpan<float> interleaved, int channels, int frames, Span<float> mono)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (interleaved.Length < frames * channels)
            throw new ArgumentException("The source holds fewer frames than asked.", nameof(interleaved));
        if (mono.Length < frames)
            throw new ArgumentException("The destination is shorter than the frames to convert.", nameof(mono));

        if (channels == 1)
        {
            for (int f = 0; f < frames; f++)
                mono[f] = Finite(interleaved[f]);
            return;
        }

        float inverseChannels = 1f / channels;
        for (int f = 0; f < frames; f++)
        {
            ReadOnlySpan<float> frame = interleaved.Slice(f * channels, channels);
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += Finite(frame[c]);
            mono[f] = sum * inverseChannels;
        }
    }

    private static float Finite(float sample) => float.IsFinite(sample) ? sample : 0f;
}
