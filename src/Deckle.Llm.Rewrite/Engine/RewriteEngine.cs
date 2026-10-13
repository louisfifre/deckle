namespace Deckle.Llm.Rewrite;

// ─── The engine seam ─────────────────────────────────────────────────────────
//
// The rewrite service is the single service every rewrite goes through:
// transcription finalization, the paragraph rewrite, the sentence stage's
// escalations. The inference engine sits behind this seam so it can change
// without the clients knowing; in-process ONNX is the decided target.
//
// The seam is synchronous by design, like IRewriteService: every caller is
// already on a background thread, and the engines' native/HTTP calls block
// anyway. The caller owns the deadline (the transcription path allows
// minutes, an interactive paragraph offer a few seconds), so the cap lives
// above the seam, not in it.

/// <summary><paramref name="Endpoint"/> and <paramref name="KeepAlive"/> are
/// engine hints that an in-process engine would ignore.
/// <paramref name="Label"/> names the caller in observability payloads and is
/// never fed to the model.</summary>
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
    /// <summary>Throws on transport failure and on cancellation; the caller
    /// owns the human-facing outcome of both.</summary>
    RewriteResult Generate(RewriteEngineRequest request, CancellationToken cancellationToken);
}
