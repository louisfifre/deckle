namespace Deckle.Llm;

// Text is null when the caller short-circuited (no engine, timeout,
// exception). The engine metrics are then zero, and only TotalMs may carry
// the elapsed wait, so check Text before reading them.
//
// LoadMs is the time the provider spent loading the model before the first
// token; a hosted API reports 0. PromptEvalMs / EvalMs split the generation
// where the provider reports it (Ollama does; a hosted API reports the whole
// call as EvalMs).
//
// PromptTokens counts the input billed at full price. A provider that caches
// prompts reports the rest apart: CacheWriteTokens were stored this call
// (billed above the input price), CacheReadTokens were read back (billed far
// below it). The whole prompt is the sum of the three; an engine that does not
// cache leaves both at zero.
public readonly record struct GenerationResult(
    string? Text,
    long    TotalMs,
    long    LoadMs,
    long    PromptEvalMs,
    long    EvalMs,
    int     PromptTokens,
    int     EvalTokens,
    int     CacheWriteTokens = 0,
    int     CacheReadTokens = 0);
