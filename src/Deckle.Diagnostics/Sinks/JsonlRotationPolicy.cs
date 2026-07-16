using System;

namespace Deckle.Diagnostics;

// Line-count rolling policy for a JsonlSink destination. When the
// active file reaches MaxLines, it is rolled into a monotonically-numbered
// generation under an `archive/` subfolder (app.jsonl → archive/app.jsonl.0001
// → archive/app.jsonl.0002 → …) and writing resumes on a fresh active file.
// Generations are never renamed or deleted — they accumulate and the user
// prunes them at will; the index keeps climbing and is zero-padded so a
// directory listing sorts in chronological order.
//
// A journal is read in lines, not bytes — "118k lines" was the unit that
// surfaced the unbounded-growth friction — so the cap is expressed in lines,
// and the active name keeps its full `.jsonl` extension with the generation
// index appended after it so every generation sorts next to the active file.
//
// Scope. Only the general application journal (app.jsonl) rolls. The dataset
// channels (latency, microphone, corpus) are append-only datasets with a
// stable cross-session contract and are never given a rotation policy —
// losing their tail would corrupt the dataset. The principle (journal rolled,
// datasets untouched) is part of the application JSONL contract.
public sealed class JsonlRotationPolicy
{
    public int MaxLines { get; }

    public JsonlRotationPolicy(int maxLines)
    {
        if (maxLines <= 0) throw new ArgumentOutOfRangeException(nameof(maxLines));
        MaxLines = maxLines;
    }
}
