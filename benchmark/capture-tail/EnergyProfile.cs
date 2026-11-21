namespace Deckle.Benchmark.CaptureTail;

// Audio-only view of the tail: how long a raw take stays silent before its end.
// A raw WAV ends RecordedTailMs after the Stop press, so with `trailing` the time
// from the last frame above a loudness threshold to the end of the file:
//   trailing < RecordedTailMs  means sound was still running at the press.
// A tail of X ms would have cut sound in the takes where trailing < Recorded - X.
// It is a proxy: breaths and weak word endings count as sound. The word-level
// answer is TailReplay.
internal static class EnergyProfile
{
    public const int RecordedTailMs = 400;
    private const int FrameMs = 20;
    private static readonly double[] ThresholdsDbfs = [-50, -45, -40];

    public static void Run(IReadOnlyList<string> wavPaths)
    {
        var trailing = ThresholdsDbfs.ToDictionary(t => t, _ => new List<double>());
        int read = 0;
        foreach (string path in wavPaths)
        {
            float[]? s = WavSamples.Read(path);
            if (s is null || s.Length < WavSamples.SampleRate) continue;
            read++;
            double[] db = WavSamples.FrameDb(s, FrameMs);
            foreach (double t in ThresholdsDbfs)
            {
                int last = Array.FindLastIndex(db, d => d >= t);
                if (last >= 0)
                    trailing[t].Add(s.Length * 1000.0 / WavSamples.SampleRate - (last + 1) * FrameMs);
            }
        }

        Stats.Result($"energy: raw takes read {read}, frame {FrameMs} ms, recorded tail {RecordedTailMs} ms");
        foreach (double t in ThresholdsDbfs)
        {
            List<double> v = trailing[t];
            Stats.Result($"energy {t} dBFS: takes with sound {v.Count}; trailing ms p5 {Stats.Percentile(v, 5):F0}, p25 {Stats.Percentile(v, 25):F0}, p50 {Stats.Percentile(v, 50):F0}, p90 {Stats.Percentile(v, 90):F0}");
            Stats.Result($"energy {t} dBFS: sound at the press {Stats.Share(v.Count(x => x < RecordedTailMs), v.Count)}; sound to the file end (<40 ms) {Stats.Share(v.Count(x => x < 40), v.Count)}");
            foreach (int x in new[] { 200, 300 })
                Stats.Result($"energy {t} dBFS: tail {x} ms would cut sound in {Stats.Share(v.Count(y => y < RecordedTailMs - x), v.Count)}");
        }
    }
}
