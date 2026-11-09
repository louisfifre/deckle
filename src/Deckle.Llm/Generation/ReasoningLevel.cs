using System.Text.Json.Serialization;

namespace Deckle.Llm;

// ─── How much a model reasons before it answers ──────────────────────────────
//
// On Claude it is two fields, the effort and the thinking mode, and the 5.5
// models only think adaptively, deciding per request whether to think at all:
// Low and Medium are the effort that adaptive thinking runs under, Off turns
// thinking off where the model allows it. Higher efforts exist but a rewrite
// has no use for them, so the knob stops at Medium. A request that names no
// level (null) keeps the engine's default.
//
// Only the Claude engine maps it. Local servers each take their own field
// (Ollama's `think`, the chat-completions `reasoning_effort`) and each model
// honours a different subset of it, so the local engines drop the level on
// purpose rather than carry one adapter per server and per model; the local
// runtime Deckle will host itself is where that knob belongs.
//
// The reasoning a model does is billed and timed as output even when its text
// is never returned, so on a rewrite, whose answer is about as long as its
// input, the level weighs on cost and latency as much as the text does.

[JsonConverter(typeof(JsonStringEnumConverter<ReasoningLevel>))]
public enum ReasoningLevel
{
    Off,
    Low,
    Medium,
}
