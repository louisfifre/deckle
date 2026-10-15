using System.Text.Json.Serialization;

namespace Deckle.Llm;

// The wire protocol a provider speaks. Two protocols cover almost the whole
// market: Anthropic's Messages API (Claude only) and the chat-completions
// shape every local server and most hosted APIs expose. Ollama's own raw mode
// is kept as a third kind for GGUF imports whose Modelfile template is broken
// (see OllamaEngine), not as the default local path.
[JsonConverter(typeof(JsonStringEnumConverter<LlmProviderKind>))]
public enum LlmProviderKind
{
    Ollama,
    OpenAiCompatible,
    Claude,
}
