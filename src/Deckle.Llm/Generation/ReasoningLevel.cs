using System.Text.Json.Serialization;

namespace Deckle.Llm;

// ─── How much a model reasons before it answers ──────────────────────────────
//
// Every family of provider has a knob for it, under its own name: Claude's
// effort and thinking mode, the chat-completions `reasoning_effort` that
// OpenAI, LM Studio and Ollama's /v1 accept, Ollama's own `think`. The four
// levels are the ones they share; each engine maps them onto its provider or
// drops them where the provider has no such knob. A request that names no
// level (null) keeps whatever the engine sent before this knob existed.
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
    High,
}
