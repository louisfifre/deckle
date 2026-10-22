using System;

namespace Deckle.Diagnostics;

// Line-count rolling policy for a JsonlSink destination. When the
// active file reaches MaxLines, it is rolled into a monotonically-numbered
// generation under an `archive/` subfolder (app.jsonl → archive/app.jsonl.0001
// → archive/app.jsonl.0002 → …) and writing resumes on a fresh active file.
// Generations are never renamed. They are kept up to MaxArchives and the
// oldest go first (lowest index); with a null cap they accumulate and the user
// prunes them by hand. The index is zero-padded so a directory listing sorts
// in chronological order. The cap is in lines because a log is read in
// lines, not bytes.
//
// Scope. Only the operational log files (setup, journal, app) roll. The dataset
// channels (latency, microphone, corpus) are append-only datasets with a
// stable cross-session contract and are never given a rotation policy —
// losing their tail would corrupt the dataset.
public sealed class JsonlRotationPolicy
{
    public int MaxLines { get; }

    // Null keeps every generation. A value is a count of generations, so
    // zero would mean "archive nothing" — a different policy, not a cap.
    public int? MaxArchives { get; }

    public JsonlRotationPolicy(int maxLines, int? maxArchives = null)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        if (maxArchives is < 1) throw new ArgumentOutOfRangeException(nameof(maxArchives));
        MaxLines = maxLines;
        MaxArchives = maxArchives;
    }
}
