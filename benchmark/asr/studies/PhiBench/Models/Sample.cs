namespace Deckle.Benchmark.PhiBench.Models;

/// <summary>
/// One sample from a Deckle telemetry corpus, mirroring the Python Sample dataclass
/// in benchmark/asr/lib/corpus.py. Field semantics are identical so the C# JSONL output
/// can be consumed without translation by the existing Python post-processors.
/// </summary>
public sealed record Sample(
    string Id,
    string AudioFile,
    string AudioPath,
    double DurationSeconds,
    string Tier,
    string ReferenceTextWhisper,
    int ReferenceWordsWhisper,
    string ReferenceTextGemini);
