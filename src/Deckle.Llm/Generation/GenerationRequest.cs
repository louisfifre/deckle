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
/// never fed to the model.</summary>
public readonly record struct GenerationRequest(
    string Model,
    string SystemPrompt,
    string UserText,
    string Label,
    double? Temperature = null,
    int? NumCtx = null,
    double? TopP = null,
    double? RepeatPenalty = null,
    string KeepAlive = "5m");
