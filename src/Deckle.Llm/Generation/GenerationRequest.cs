namespace Deckle.Llm;

// ─── The generation seam, request side ───────────────────────────────────────
//
// One request shape for every engine: a system prompt, the text to work on,
// and generation hints. The provider (endpoint, credentials) is not part of
// the request: an engine is bound to its provider when it is built, so a
// caller only names the model.

/// <summary><paramref name="Temperature"/>, <paramref name="NumCtx"/>,
/// <paramref name="TopP"/>, <paramref name="RepeatPenalty"/> and
/// <paramref name="KeepAlive"/> are hints a local runtime honours and a hosted
/// API may ignore or reject; an engine drops what its provider does not take.
/// <paramref name="Label"/> names the caller in observability payloads and is
/// never fed to the model.
/// <paramref name="CachePrompt"/> asks a hosted API that caches prompts to
/// keep the whole request (system prompt and text) for the next identical
/// call: the benchmark repeats one request many times in a row and reads it
/// back at a tenth of the input price or less. The app leaves it off, since
/// its dictations rarely repeat a prompt within the cache's five minutes and
/// each write costs a quarter more than an uncached call.
/// <paramref name="Reasoning"/> says how much the model reasons before it
/// answers; null keeps the engine's own default, and an engine whose provider
/// has no such knob drops it, as with the hints above.</summary>
public readonly record struct GenerationRequest(
    string Model,
    string SystemPrompt,
    string UserText,
    string Label,
    double? Temperature = null,
    int? NumCtx = null,
    double? TopP = null,
    double? RepeatPenalty = null,
    string KeepAlive = "5m",
    bool CachePrompt = false,
    ReasoningLevel? Reasoning = null);
