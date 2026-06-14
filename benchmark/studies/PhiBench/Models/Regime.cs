namespace Deckle.Benchmark.PhiBench.Models;

/// <summary>
/// One transcription regime. Mirrors the TOML records in
/// benchmark/prompts/transcription/voxtral_validation.toml.
///
/// For Phi-4 (which has no canonical [TRANSCRIBE] token like Voxtral),
/// regimes control the user prompt explicitly. An empty Prompt is sent
/// as empty; the transcriber default is only for callers that pass null.
/// </summary>
public sealed record Regime(
    string Name,
    string Label,
    string Prompt,
    string SystemPrompt);
