using Deckle.Diagnostics;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Rewrite service ─────────────────────────────────────────────────────────
//
// The single service every rewrite goes through, whoever asks (module
// CONTEXT.md). Owns what sits above the engine seam: the profile-driven
// request, the hard deadline, and the human-facing outcome of a failure —
// the overlay feedback belongs to the service, not to whichever engine
// happened to run. The engine behind IRewriteEngine (Ollama today, ONNX as
// the decided target) is swappable without the clients noticing.
//
// Designed to be called from a background thread — the engines block, and
// .GetAwaiter().GetResult() inside them is safe there (no synchronization
// context on those threads).

// Result returned by Rewrite — pairs the rewritten text with the structured
// metrics the engine reports. Wrapping them avoids losing the timings to the
// Verbose log line when the caller wants them in a payload (LatencyPayload,
// benchmark exports). Every numeric field is in ms / tokens to stay aligned
// with the logging inventory vocabulary. All zeros when Rewrite
// short-circuits (no model configured, timeout, exception) — the caller is
// expected to check Text != null before reading metrics.
public readonly record struct RewriteResult(
    string?      Text,
    long         TotalMs,
    long         OllamaLoadMs,
    long         PromptEvalMs,
    long         EvalMs,
    int          PromptTokens,
    int          EvalTokens);

public interface IRewriteService
{
    RewriteResult Rewrite(string text, string endpoint, RewriteProfile profile);
}

public class RewriteService : IRewriteService
{
    // Hard cap on a single Rewrite call. Generous: leaves room for slow CPU-only
    // Ollama setups on big transcripts (20 min audio, 16 K context), but still
    // guards against a stuck worker. Internal: the Ollama busy-poll log line
    // quotes it as context next to the elapsed wait.
    internal static readonly TimeSpan REWRITE_HARD_CAP = TimeSpan.FromMinutes(15);

    readonly IRewriteEngine _engine;

    public RewriteService() : this(new OllamaEngine()) { }

    /// <summary>The engine seam: hand in an OnnxEngine (or a test double)
    /// and every client of the service switches with it.</summary>
    public RewriteService(IRewriteEngine engine) => _engine = engine;

    public RewriteResult Rewrite(string text, string endpoint, RewriteProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            DeckleLlmSource.Log.RewriteSkippedNoModel();
            DeckleLlmSource.Log.RewriteSkippedNoModelDetail(profile.Name);
            return default;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(REWRITE_HARD_CAP);
        try
        {
            var request = new RewriteEngineRequest(
                Endpoint:      endpoint,
                Model:         profile.Model,
                SystemPrompt:  profile.SystemPrompt,
                UserText:      text,
                Label:         profile.Name,
                Temperature:   profile.Temperature,
                NumCtx:        profile.NumCtxK * 1024,   // stored in K
                TopP:          profile.TopP,
                RepeatPenalty: profile.RepeatPenalty);
            return _engine.Generate(request, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            sw.Stop();
            // Cross-cutting Cancellation sub-provider: REWRITE_HARD_CAP fired,
            // this is a hard timeout.
            DeckleCancellationSource.Log.OperationCancelled(
                "llm-rewrite", "timeout", (int)sw.ElapsedMilliseconds);
            DeckleLlmSource.Log.RewriteTimeout();
            DeckleLlmSource.Log.RewriteTimeoutDetail(REWRITE_HARD_CAP.TotalMinutes, profile.Name, profile.Model);
            // severity 1 = Warning, role 1 = Overlay.
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    "Rewriter took too long",
                body:     $"Over {REWRITE_HARD_CAP.TotalMinutes:F0} min. Raw transcript copied.",
                role:     1);
            return new RewriteResult(null, sw.ElapsedMilliseconds, 0, 0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            sw.Stop();
            DeckleLlmSource.Log.RewriteUnavailable();
            DeckleLlmSource.Log.RewriteUnavailableDetail(ex.GetType().Name, ex.Message, profile.Name, profile.Model);
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    "Rewriter unavailable",
                body:     "Ollama unreachable. Raw transcript copied.",
                role:     1);
            return new RewriteResult(null, sw.ElapsedMilliseconds, 0, 0, 0, 0, 0);
        }
    }
}
