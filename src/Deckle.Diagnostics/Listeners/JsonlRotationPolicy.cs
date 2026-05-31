using System;

namespace Deckle.Diagnostics.Listeners;

// Size-based rolling policy for a JsonlEventListener destination. When
// the active file would cross MaxBytes, it is rolled to a numbered
// generation (app.jsonl → app.1.jsonl → … → app.{MaxGenerations}.jsonl)
// and writing resumes on a fresh active file; the generation beyond
// MaxGenerations is dropped. Total on-disk footprint is therefore bounded
// to roughly (MaxGenerations + 1) × MaxBytes.
//
// Scope. Only the general application journal (app.jsonl) rolls. The
// dataset channels (latency, microphone, corpus) are append-only ML
// datasets with a stable cross-session contract (ADR-0011) and are never
// given a rotation policy — losing their tail would corrupt the dataset.
// The decision and the chosen bound live in ADR-0017.
public sealed class JsonlRotationPolicy
{
    public long MaxBytes { get; }
    public int MaxGenerations { get; }

    public JsonlRotationPolicy(long maxBytes, int maxGenerations)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxGenerations < 1) throw new ArgumentOutOfRangeException(nameof(maxGenerations));
        MaxBytes = maxBytes;
        MaxGenerations = maxGenerations;
    }
}
