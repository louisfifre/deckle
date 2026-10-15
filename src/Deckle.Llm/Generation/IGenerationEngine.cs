namespace Deckle.Llm;

// ─── The generation seam ─────────────────────────────────────────────────────
//
// Every consumer that needs a model to write text goes through this seam:
// today the rewrite service (transcription finalization, the paragraph
// rewrite, the sentence stage's escalations), tomorrow any module that asks a
// model a question. The engine behind it is one provider's transport: Ollama,
// a chat-completions server (LM Studio, llama.cpp, a hosted API), Claude. The
// provider list (LlmProvider) names which one serves a request, so the engine
// can change without the consumers knowing.
//
// The seam is synchronous by design: every caller is already on a background
// thread, and the engines' HTTP calls block anyway. The caller owns the
// deadline (the transcription path allows minutes, an interactive paragraph
// offer a few seconds), so the cap lives above the seam, not in it.

public interface IGenerationEngine
{
    /// <summary>Throws on transport failure and on cancellation; the caller
    /// owns the human-facing outcome of both.</summary>
    GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken);
}
