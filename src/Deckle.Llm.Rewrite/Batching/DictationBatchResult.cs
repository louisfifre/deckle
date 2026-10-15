namespace Deckle.Llm.Rewrite;

// The assembled outcome of a batched rewrite: the text to paste, how many
// batches it took, how many kept their raw text, and the LLM figures summed
// over every batch for the take's telemetry. LlmMs is the sum of each
// Rewrite call's wall time; the batches run in parallel, so it measures the
// work done, not the wait the speaker saw.
public sealed record DictationBatchResult(
    string Text,
    int    BatchCount,
    int    FallbackCount,
    long   LlmMs,
    long   LlmLoadMs,
    long   LlmPromptEvalMs,
    long   LlmEvalMs,
    int    LlmPromptTokens,
    int    LlmEvalTokens);
