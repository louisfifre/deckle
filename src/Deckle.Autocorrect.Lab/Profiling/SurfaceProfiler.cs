using System;
using System.Collections.Generic;
using System.Linq;

namespace Deckle.Autocorrect.Lab;

// Splits the typed-sentence corpus into surface profiles: one per-application
// portrait of how typing behaves there, computed from each record's closure and
// timing. Everything here is measured, never configured: the profiles show where
// the sentence stage arrives too late (Enter-heavy surfaces) and give the pause
// pass its gap distribution. Pure over parsed corpus entries; the gesture owns
// file I/O and report placement.
public static class SurfaceProfiler
{
    public static IReadOnlyList<SurfaceProfile> Profile(IEnumerable<CorpusEntry> entries)
    {
        var buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
        foreach (CorpusEntry entry in entries)
        {
            string process = entry.Process.Length > 0 ? entry.Process : "(unknown)";
            if (!buckets.TryGetValue(process, out Bucket? bucket))
                buckets[process] = bucket = new Bucket();
            bucket.Add(entry.Record);
        }

        return buckets
            .Select(kv => kv.Value.ToProfile(kv.Key))
            .OrderByDescending(p => p.Sentences)
            .ToList();
    }

    // The pause bar a surface earns from its own measurements. The formula is a
    // guess awaiting calibration on real closure and timing data, quarantined
    // here so recalibration touches nothing else. The bar is the p99 of the
    // surface's own inter-commit gaps: a pause unusual for the way the user
    // types there. 0 = not qualified; the pause pass stays disarmed there.
    public static int ProvisionalPauseThresholdMs(SurfaceProfile profile)
    {
        const int minTimedSentences = 30;
        int closed = profile.EnterClosed + profile.SentenceClosed;
        if (profile.TimedSentences < minTimedSentences || closed == 0)
            return 0;
        if (profile.EnterClosed * 2 < closed)
            return 0; // Enter does not dominate — the true closure arrives in time
        return profile.Gaps.P99;
    }

    public static SurfaceProfile Overall(IEnumerable<CorpusEntry> entries)
    {
        var bucket = new Bucket();
        foreach (CorpusEntry entry in entries)
            bucket.Add(entry.Record);
        return bucket.ToProfile("(all)");
    }

    private sealed class Bucket
    {
        private int _sentences, _enters, _interrupted, _other, _words;
        private readonly List<int> _gaps = new();
        private int _timedSentences;

        public void Add(SentenceCorpus.SentenceRecord record)
        {
            switch (record.Closure)
            {
                case "sentence": _sentences++; break;
                case "enter": _enters++; break;
                case "interrupted": _interrupted++; break;
                default: _other++; break;
            }

            // Timing is the exact slot count when present ("0,340,1220" = three
            // slots); the typed side's whitespace split approximates it for
            // records without the field.
            if (record.Timing.Length > 0)
            {
                _timedSentences++;
                string[] gaps = record.Timing.Split(',');
                _words += gaps.Length;
                // The first slot's "0" is a placeholder, not a measured gap.
                for (int i = 1; i < gaps.Length; i++)
                    if (int.TryParse(gaps[i], out int gap))
                        _gaps.Add(gap);
            }
            else
            {
                _words += record.Typed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        public SurfaceProfile ToProfile(string process) => new(
            process,
            Sentences: _sentences + _enters + _interrupted + _other,
            Words: _words,
            SentenceClosed: _sentences,
            EnterClosed: _enters,
            Interrupted: _interrupted,
            OtherClosed: _other,
            TimedSentences: _timedSentences,
            Gaps: GapStats.Cut(_gaps));
    }
}

// Sentences counts every record whatever its closure; TimedSentences the subset
// carrying a timing string, the gap stats' population. Gaps are inter-slot
// (word-commit to word-commit) milliseconds.
public sealed record SurfaceProfile(
    string Process,
    int Sentences,
    int Words,
    int SentenceClosed,
    int EnterClosed,
    int Interrupted,
    int OtherClosed,
    int TimedSentences,
    GapStats Gaps);

// Nearest-rank percentiles over the collected gaps; Count 0 means the surface
// never carried a timing string and every cut reads 0.
public readonly record struct GapStats(int Count, int P50, int P75, int P90, int P99, int Max)
{
    public static GapStats Cut(List<int> gaps)
    {
        if (gaps.Count == 0) return new GapStats(0, 0, 0, 0, 0, 0);
        gaps.Sort();
        return new GapStats(
            gaps.Count,
            AtRank(gaps, 0.50),
            AtRank(gaps, 0.75),
            AtRank(gaps, 0.90),
            AtRank(gaps, 0.99),
            gaps[^1]);
    }

    private static int AtRank(List<int> sorted, double percentile) =>
        sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(percentile * sorted.Count) - 1)];
}
