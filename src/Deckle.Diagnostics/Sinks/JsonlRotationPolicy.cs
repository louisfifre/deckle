using System;

namespace Deckle.Diagnostics;

// Line-count rolling policy for a JsonlSink destination. When the
// active file reaches MaxLines, it is rolled into a monotonically-numbered
// generation under an `archive/` subfolder (app.jsonl → archive/app.jsonl.0001
// → archive/app.jsonl.0002 → …) and writing resumes on a fresh active file.
// Generations are never renamed or deleted — they accumulate and the user
// prunes them at will; the index is zero-padded so a directory listing sorts
// in chronological order. The cap is in lines because a journal is read in
// lines, not bytes.
//
// Scope. Only the general application journal (app.jsonl) rolls. The dataset
// channels (latency, microphone, corpus) are append-only datasets with a
// stable cross-session contract and are never given a rotation policy —
// losing their tail would corrupt the dataset.
public sealed class JsonlRotationPolicy
{
    public int MaxLines { get; }

    public JsonlRotationPolicy(int maxLines)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        MaxLines = maxLines;
    }
}
