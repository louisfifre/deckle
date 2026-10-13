using Deckle.Diagnostics;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Rewrite service ─────────────────────────────────────────────────────────
//
// Owns what sits above the engine seam: the profile-driven request, the hard
// deadline, and the human-facing outcome of a failure. The overlay feedback
// belongs to the service, not to whichever engine happened to run.
//
// Call it from a background thread: the engines block, and
// .GetAwaiter().GetResult() inside them is safe there (no synchronization
// context on those threads).

// Text is null when Rewrite short-circuits (no model configured, timeout,
// exception). The engine metrics are then zero, and only TotalMs may carry the
// elapsed wait, so check Text before reading them.
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
    RewriteResult RewriteParagraph(string paragraph, string endpoint, CancellationToken cancellationToken);
    RewriteResult RewriteSentence(string sentence, string endpoint, CancellationToken cancellationToken);
}

public class RewriteService : IRewriteService
{
    // Generous enough for slow CPU-only Ollama setups on long transcripts, but
    // still guards against a stuck worker. Internal because the Ollama
    // busy-poll log line quotes it next to the elapsed wait.
    internal static readonly TimeSpan REWRITE_HARD_CAP = TimeSpan.FromMinutes(15);

    readonly IRewriteEngine _engine;

    public RewriteService() : this(new OllamaEngine()) { }

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

    /// <summary>Runs the fixed paragraph-retaille contract. Unlike transcript
    /// rewriting, failures stay silent at the UI boundary: an interactive
    /// suggestion is optional and the caller simply offers nothing.</summary>
    public RewriteResult RewriteParagraph(
        string paragraph,
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            return _engine.Generate(
                ParagraphRewrite.BuildRequest(paragraph, endpoint, ParagraphRewrite.Model),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The interactive deadline or a keystroke that made the request
            // stale: both are expected, so not even logged.
            return default;
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.RewriteUnavailable();
            DeckleLlmSource.Log.RewriteUnavailableDetail(
                ex.GetType().Name,
                ex.Message,
                ParagraphRewrite.Label,
                ParagraphRewrite.Model);
            return default;
        }
    }

    /// <summary>Generates one minimal whole-sentence correction proposal. The
    /// result is untrusted model output: callers must gate and verify it before
    /// treating any edit as a Correction.</summary>
    public RewriteResult RewriteSentence(
        string sentence,
        string endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            return _engine.Generate(
                SentenceRewrite.BuildRequest(sentence, endpoint),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return default;
        }
        catch (Exception ex)
        {
            DeckleLlmSource.Log.RewriteUnavailable();
            DeckleLlmSource.Log.RewriteUnavailableDetail(
                ex.GetType().Name,
                ex.Message,
                SentenceRewrite.Label,
                SentenceRewrite.Model);
            return default;
        }
    }
}
