namespace Deckle.Benchmark.PhiBench.Models;

/// <summary>
/// One transcription regime. Mirrors the TOML records in
/// benchmark/prompts/transcription/voxtral_validation.toml.
///
/// For Phi-4 (which has no canonical [TRANSCRIBE] token like Voxtral),
/// even T1_baseline must use a real instruction prompt. Empty Prompt+SystemPrompt
/// will be coerced into a default "Transcribe this audio in French." at the
/// transcriber level — see Phi4Transcriber.
/// </summary>
public sealed record Regime(
    string Name,
    string Label,
    string Prompt,
    string SystemPrompt);
