namespace Deckle.Benchmark.CaptureTail;

// PCM16 mono 16 kHz WAV as WavCorpusWriter writes it, read back to floats.
internal static class WavSamples
{
    public const int SampleRate = 16_000;

    public static float[]? Read(string path)
    {
        try
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || b[0] != 'R' || b[8] != 'W') return null;
            int pos = 12, dataAt = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                int len = BitConverter.ToInt32(b, pos + 4);
                if (b[pos] == 'd' && b[pos + 1] == 'a' && b[pos + 2] == 't' && b[pos + 3] == 'a')
                {
                    dataAt = pos + 8;
                    dataLen = Math.Min(len, b.Length - dataAt);
                    break;
                }
                pos += 8 + len;
            }
            if (dataAt < 0) return null;
            var s = new float[dataLen / 2];
            for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(b, dataAt + 2 * i) / 32768f;
            return s;
        }
        catch (IOException) { return null; }
    }

    // Loudness per 20 ms frame, in dBFS.
    public static double[] FrameDb(float[] s, int frameMs)
    {
        int frame = SampleRate / 1000 * frameMs;
        int n = s.Length / frame;
        var db = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0;
            for (int j = i * frame; j < (i + 1) * frame; j++) acc += (double)s[j] * s[j];
            db[i] = 10 * Math.Log10(acc / frame + 1e-12);
        }
        return db;
    }
}
