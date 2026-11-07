namespace Deckle.Audio.Internal;

// How a capture endpoint encodes one sample in its mix format. Shared mode
// hands the application the engine's mix format, which Deckle does not choose:
// 32-bit float is what Windows normally mixes in, and the integer encodings
// appear when a driver exposes its native format in raw mode.
internal enum SampleEncoding
{
    Float32,
    Float64,
    Pcm16,
    Pcm24,
    Pcm32,
}

// The device's own format: the one thing the rest of the capture path is
// converted from. Everything downstream of the converter is 16 kHz mono float.
internal readonly record struct CaptureFormat(int SampleRate, int Channels, SampleEncoding Encoding)
{
    public int BytesPerSample => Encoding switch
    {
        SampleEncoding.Pcm16   => 2,
        SampleEncoding.Pcm24   => 3,
        SampleEncoding.Float64 => 8,
        _                      => 4,
    };

    // Bytes of one frame: one sample per channel.
    public int BlockAlign => Channels * BytesPerSample;

    // WAVEFORMATEX format tags and the WAVEFORMATEXTENSIBLE escape (mmreg.h).
    private const ushort FormatPcm        = 0x0001;
    private const ushort FormatIeeeFloat  = 0x0003;
    private const ushort FormatExtensible = 0xFFFE;

    // The sub-format GUID of an extensible format starts with the plain format
    // tag (KSDATAFORMAT_SUBTYPE_PCM is {00000001-0000-0010-8000-00AA00389B71},
    // IEEE_FLOAT {00000003-...}), so the first two bytes of it are enough.
    private const int ExtensibleSubFormatOffset = 24;

    // Reads a WAVEFORMATEX or WAVEFORMATEXTENSIBLE as the audio engine returns
    // it. False for an encoding Deckle does not convert (compressed, 8-bit,
    // 12-bit...), which the capture then reports as an unusable microphone
    // instead of recording noise.
    public static bool TryParse(ReadOnlySpan<byte> waveFormat, out CaptureFormat format)
    {
        format = default;
        if (waveFormat.Length < 16) return false;

        ushort tag      = ReadUInt16(waveFormat, 0);
        int channels    = ReadUInt16(waveFormat, 2);
        int sampleRate  = (int)ReadUInt32(waveFormat, 4);
        int bits        = ReadUInt16(waveFormat, 14);

        if (tag == FormatExtensible)
        {
            if (waveFormat.Length < ExtensibleSubFormatOffset + 2) return false;
            tag = ReadUInt16(waveFormat, ExtensibleSubFormatOffset);
        }

        if (channels <= 0 || sampleRate <= 0) return false;

        SampleEncoding? encoding = (tag, bits) switch
        {
            (FormatIeeeFloat, 32) => SampleEncoding.Float32,
            (FormatIeeeFloat, 64) => SampleEncoding.Float64,
            (FormatPcm, 16)       => SampleEncoding.Pcm16,
            (FormatPcm, 24)       => SampleEncoding.Pcm24,
            (FormatPcm, 32)       => SampleEncoding.Pcm32,
            _                     => null,
        };
        if (encoding is null) return false;

        format = new CaptureFormat(sampleRate, channels, encoding.Value);
        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
        => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
}
