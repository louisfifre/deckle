namespace Deckle.Llm.Rewrite;

// ─── The engine seam ─────────────────────────────────────────────────────────
//
// The rewrite service is the single service every rewrite goes through —
// transcription finalization, the paragraph rewrite, the sentence stage's
// escalations (module CONTEXT.md). The inference engine sits behind this
// seam and can change without the clients knowing: Ollama today, in-process
// ONNX as the decided target. An OnnxEngine implements this same interface
// and drops in; nothing above the seam moves.
//
// The seam is synchronous by design, like IRewriteService: every caller is
// already on a background thread, and the engines' native/HTTP calls block
// anyway. Cancellation is the caller's: the caller owns the deadline (the
// transcription path caps at 15 min, an interactive paragraph offer wants a
// few seconds), so the cap lives above the seam, not in it.

/// <summary>One generation request. <paramref name="Endpoint"/> and
/// <paramref name="KeepAlive"/> are engine hints — Ollama reads them, an
/// in-process engine ignores them. <paramref name="Label"/> names the caller
/// in observability payloads (profile name, "paragraph", …), never fed to
/// the model.</summary>
public readonly record struct RewriteEngineRequest(
    string Endpoint,
    string Model,
    string SystemPrompt,
    string UserText,
    string Label,
    double? Temperature = null,
    int? NumCtx = null,
    double? TopP = null,
    double? RepeatPenalty = null,
    string KeepAlive = "5m");

public interface IRewriteEngine
{
    /// <summary>Runs one generation. Returns the rewritten text plus engine
    /// metrics; throws on transport failure and on cancellation — the caller
    /// owns the human-facing outcome of both.</summary>
    RewriteResult Generate(RewriteEngineRequest request, CancellationToken cancellationToken);
}
