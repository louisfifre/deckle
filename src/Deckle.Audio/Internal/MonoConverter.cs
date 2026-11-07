using System.Buffers.Binary;

namespace Deckle.Audio.Internal;

// Interleaved device samples to one mono float stream, in the device's own
// sample rate. The first of the two steps that turn a mix format into the
// 16 kHz mono float every consumer expects (the second is StreamingResampler).
//
// A microphone with several channels (a stereo headset, a mic array exposed in
// raw mode) becomes the arithmetic mean of its channels, in channel order:
// plain, deterministic, and identical on every machine, which is the point of
// doing it here instead of letting Windows mix. A mean cannot clip, since each
// channel is already within [-1, 1].
internal static class MonoConverter
{
    // Integer samples are left-justified in their container: 24-bit and 32-bit
    // PCM both use the top of the word, so the divisors are the full-scale
    // values of the container, never of the valid bits.
    private const float Pcm16Scale = 1f / 32768f;
    private const double Pcm24Scale = 1.0 / 8388608.0;
    private const double Pcm32Scale = 1.0 / 2147483648.0;

    // The number of whole frames in a block of device bytes; a trailing partial
    // frame is ignored, as the audio engine never delivers one.
    public static int FrameCount(ReadOnlySpan<byte> interleaved, CaptureFormat format)
        => interleaved.Length / format.BlockAlign;

    // Writes FrameCount(interleaved) samples into mono. The destination must be
    // at least that long.
    public static int ToMono(ReadOnlySpan<byte> interleaved, CaptureFormat format, Span<float> mono)
    {
        int frames = FrameCount(interleaved, format);
        if (mono.Length < frames)
            throw new ArgumentException("The destination is shorter than the frames to convert.", nameof(mono));

        int channels = format.Channels;
        int stride = format.BytesPerSample;
        float inverseChannels = 1f / channels;

        for (int f = 0; f < frames; f++)
        {
            ReadOnlySpan<byte> frame = interleaved.Slice(f * format.BlockAlign, format.BlockAlign);
            if (channels == 1)
            {
                mono[f] = ReadSample(frame, format.Encoding);
                continue;
            }

            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += ReadSample(frame.Slice(c * stride, stride), format.Encoding);
            mono[f] = sum * inverseChannels;
        }
        return frames;
    }

    private static float ReadSample(ReadOnlySpan<byte> bytes, SampleEncoding encoding) => encoding switch
    {
        SampleEncoding.Float32 => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        SampleEncoding.Float64 => (float)BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        SampleEncoding.Pcm16   => BinaryPrimitives.ReadInt16LittleEndian(bytes) * Pcm16Scale,
        SampleEncoding.Pcm24   => (float)(((bytes[0] << 8) | (bytes[1] << 16) | (bytes[2] << 24)) * Pcm32Scale),
        _                      => (float)(BinaryPrimitives.ReadInt32LittleEndian(bytes) * Pcm32Scale),
    };
}
