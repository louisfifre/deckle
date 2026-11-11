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
/// <paramref name="Cache"/> asks a hosted API that caches prompts to keep
/// part of the request for the calls that follow; <see cref="PromptCacheScope"/>
/// says which part and who asks for it. An engine whose provider has no such
/// cache drops it.
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
    PromptCacheScope Cache = PromptCacheScope.None,
    ReasoningLevel? Reasoning = null);

/// <summary>How much of a request a hosted API keeps for the next call. The
/// cache matches a request from its first byte, so only calls that start
/// alike read it back, within about five minutes of the last one. A read
/// costs a tenth of the input price or less; a write costs a quarter more
/// than an uncached call, so a scope pays only where a second call follows.</summary>
public enum PromptCacheScope
{
    /// <summary>Nothing is kept. A whole-dictation rewrite uses this: the
    /// next dictation rarely comes within the five minutes, and it would
    /// pay the write for nothing.</summary>
    None,

    /// <summary>The system prompt alone. The batches of one dictation use
    /// this: each sends the same profile prompt with a new text, less than a
    /// minute after the one before.</summary>
    SystemPrompt,

    /// <summary>The system prompt and the text together. The benchmark uses
    /// this: it repeats one whole request many times in a row.</summary>
    WholeRequest,
}
