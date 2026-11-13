using System;

namespace Deckle.Diagnostics;

// Line-count rolling policy for a JsonlSink destination. When the
// active file reaches MaxLines, it is rolled into a monotonically-numbered
// generation under an `archive/` subfolder (trace.jsonl → archive/trace.jsonl.0001
// → archive/trace.jsonl.0002 → …) and writing resumes on a fresh active file.
// Generations are never renamed. They are kept up to MaxArchives and the
// oldest go first (lowest index); with a null cap they accumulate and the user
// prunes them by hand. The index is zero-padded so a directory listing sorts
// in chronological order. The cap is in lines because a log is read in
// lines, not bytes.
//
// The archive cap may be a setting the person changes while the sink runs
// (the trace's retention): the reader form is read at each roll and at start,
// so a lowered cap trims at the next roll without rebuilding the sink.
//
// Scope. Only the operational log files (setup, log, trace) roll. The dataset
// channels (latency, microphone, corpus) are append-only datasets with a
// stable cross-session contract and are never given a rotation policy —
// losing their tail would corrupt the dataset.
public sealed class JsonlRotationPolicy
{
    private readonly Func<int?> _maxArchives;

    public int MaxLines { get; }

    // Null keeps every generation. A value is a count of generations, so
    // zero would mean "archive nothing" — a different policy, not a cap. A
    // reader that answers below one or throws reads as the safest cap that
    // still keeps history: one generation.
    public int? MaxArchives
    {
        get
        {
            int? cap;
            try { cap = _maxArchives(); }
            catch { return 1; }
            return cap is < 1 ? 1 : cap;
        }
    }

    public JsonlRotationPolicy(int maxLines, int? maxArchives = null)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxArchives is < 1) throw new ArgumentOutOfRangeException(nameof(maxArchives));
        MaxLines = maxLines;
        _maxArchives = () => maxArchives;
    }

    public JsonlRotationPolicy(int maxLines, Func<int?> maxArchives)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        MaxLines = maxLines;
        _maxArchives = maxArchives ?? throw new ArgumentNullException(nameof(maxArchives));
    }
}
