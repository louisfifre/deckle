namespace Deckle.Llm.Rewrite;

// The assembled outcome of a batched rewrite: the text of the take, how many
// batches it took, how many kept their raw text, and the LLM figures summed
// over every batch for the take's telemetry. LlmMs is the sum of each
// Rewrite call's wall time; the batches run in parallel, so it measures the
// work done, not the wait the speaker saw. LlmPromptTokens is the input
// billed at the plain price; what the provider's prompt cache stored and read
// back over the take comes apart, as in GenerationResult.
public sealed record DictationBatchResult(
    string Text,
    int    BatchCount,
    int    FallbackCount,
    long   LlmMs,
    long   LlmLoadMs,
    long   LlmPromptEvalMs,
    long   LlmEvalMs,
    int    LlmPromptTokens,
    int    LlmEvalTokens,
    int    LlmCacheWriteTokens = 0,
    int    LlmCacheReadTokens = 0);
