namespace Deckle.Benchmark.PhiBench;

/// <summary>
/// Minimal WAV header reader to compute audio duration in seconds without
/// pulling in NAudio. Handles standard PCM WAV files; falls back to a
/// byte-rate-based estimate if the data chunk is unparseable.
/// </summary>
internal static class WavHeader
{
    /// <summary>Reads the RIFF/fmt chunks and returns duration in seconds.
    /// Returns 0 if the file is not a valid PCM WAV.</summary>
    public static double GetDurationSeconds(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        if (new string(br.ReadChars(4)) != "RIFF") return 0.0;
        _ = br.ReadInt32(); // file size - 8
        if (new string(br.ReadChars(4)) != "WAVE") return 0.0;

        ushort numChannels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        uint dataSize = 0;

        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            var chunkId = new string(br.ReadChars(4));
            var chunkSize = br.ReadUInt32();
            if (chunkId == "fmt ")
            {
                _ = br.ReadUInt16();      // audio format
                numChannels = br.ReadUInt16();
                sampleRate = br.ReadUInt32();
                _ = br.ReadUInt32();      // byte rate
                _ = br.ReadUInt16();      // block align
                bitsPerSample = br.ReadUInt16();
                var consumed = 16u;
                if (chunkSize > consumed) br.ReadBytes((int)(chunkSize - consumed));
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                break;
            }
            else
            {
                br.ReadBytes((int)chunkSize);
            }
        }

        if (sampleRate == 0 || numChannels == 0 || bitsPerSample == 0) return 0.0;
        var bytesPerSample = bitsPerSample / 8;
        var totalSamples = dataSize / (uint)(bytesPerSample * numChannels);
        return totalSamples / (double)sampleRate;
    }
}
