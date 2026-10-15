using Deckle.Diagnostics;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Rewrite service ─────────────────────────────────────────────────────────
//
// Owns what sits above the generation seam: the profile-driven request, the
// provider lookup, the hard deadline, and the human-facing outcome of a
// failure. The overlay feedback belongs to the service, not to whichever
// engine happened to run.
//
// Call it from a background thread: the engines block, and
// .GetAwaiter().GetResult() inside them is safe there (no synchronization
// context on those threads).

public interface IRewriteService
{
    GenerationResult Rewrite(string text, RewriteProfile profile);
    GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken);
    GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken);
}

public class RewriteService : IRewriteService
{
    readonly IGenerationEngineResolver _engines;
    readonly Func<string, LlmProvider?> _findProvider;

    /// <summary>The production service: engines resolved from the provider
    /// list the Settings page edits, keys read from the default vault.</summary>
    public RewriteService()
        : this(() => LlmSettingsService.Instance.Current.Providers) { }

    // The resolver and the lookup read the same list, so the provider a
    // failure message names is the one the engine was built from.
    RewriteService(Func<IReadOnlyList<LlmProvider>> providers)
        : this(LlmProviderEngines.CreateDefault(providers), id => FindProvider(providers(), id)) { }

    /// <param name="findProvider">Looks a provider up by id so a failure
    /// message can name it; without it the message says "The provider".</param>
    public RewriteService(IGenerationEngineResolver engines, Func<string, LlmProvider?>? findProvider = null)
    {
        _engines = engines;
        _findProvider = findProvider ?? (_ => null);
    }

    // Same match as LlmProviderEngines.Resolve: exact Id, first wins.
    static LlmProvider? FindProvider(IReadOnlyList<LlmProvider> providers, string id)
    {
        foreach (var p in providers)
        {
            if (p.Id == id) return p;
        }
        return null;
    }

    public GenerationResult Rewrite(string text, RewriteProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            DeckleLlmSource.Log.RewriteSkippedNoModel();
            DeckleLlmSource.Log.RewriteSkippedNoModelDetail(profile.Name);
            return default;
        }

        IGenerationEngine? engine = _engines.Resolve(profile.ProviderId);
        if (engine is null)
        {
            DeckleLlmSource.Log.RewriteSkippedNoProvider();
            DeckleLlmSource.Log.RewriteSkippedNoProviderDetail(profile.Name, profile.ProviderId);
            var unresolved = RewriteFailureMessage.ForUnresolvedProvider(profile, _findProvider(profile.ProviderId));
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    unresolved.Title,
                body:     unresolved.Body,
                role:     1);
            return default;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(GenerationDeadline.HardCap);
        try
        {
            var request = new GenerationRequest(
                Model:         profile.Model,
                SystemPrompt:  profile.SystemPrompt,
                UserText:      text,
                Label:         profile.Name,
                Temperature:   profile.Temperature,
                NumCtx:        profile.NumCtxK * 1024,   // stored in K
                TopP:          profile.TopP,
                RepeatPenalty: profile.RepeatPenalty);
            return engine.Generate(request, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            sw.Stop();
            DeckleCancellationSource.Log.OperationCancelled(
                "llm-rewrite", "timeout", (int)sw.ElapsedMilliseconds);
            DeckleLlmSource.Log.RewriteTimeout();
            DeckleLlmSource.Log.RewriteTimeoutDetail(GenerationDeadline.HardCap.TotalMinutes, profile.Name, profile.Model);
            // severity 1 = Warning, role 1 = Overlay.
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    "Rewriter took too long",
                body:     $"Over {GenerationDeadline.HardCap.TotalMinutes:F0} min. Raw transcript copied.",
                role:     1);
            return new GenerationResult(null, sw.ElapsedMilliseconds, 0, 0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            sw.Stop();
            DeckleLlmSource.Log.RewriteUnavailable();
            DeckleLlmSource.Log.RewriteUnavailableDetail(ex.GetType().Name, ex.Message, profile.Name, profile.Model);
            var failure = RewriteFailureMessage.ForException(profile, _findProvider(profile.ProviderId), ex);
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    failure.Title,
                body:     failure.Body,
                role:     1);
            return new GenerationResult(null, sw.ElapsedMilliseconds, 0, 0, 0, 0, 0);
        }
    }

    /// <summary>Runs the fixed paragraph-retaille contract. Unlike transcript
    /// rewriting, failures stay silent at the UI boundary: an interactive
    /// suggestion is optional and the caller simply offers nothing.</summary>
    public GenerationResult RewriteParagraph(
        string paragraph,
        CancellationToken cancellationToken)
    {
        IGenerationEngine? engine = _engines.Resolve(ParagraphRewrite.ProviderId);
        if (engine is null)
        {
            DeckleLlmSource.Log.RewriteSkippedNoProvider();
            DeckleLlmSource.Log.RewriteSkippedNoProviderDetail(ParagraphRewrite.Label, ParagraphRewrite.ProviderId);
            return default;
        }

        try
        {
            return engine.Generate(
                ParagraphRewrite.BuildRequest(paragraph, ParagraphRewrite.Model),
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
    public GenerationResult RewriteSentence(
        string sentence,
        CancellationToken cancellationToken)
    {
        IGenerationEngine? engine = _engines.Resolve(SentenceRewrite.ProviderId);
        if (engine is null)
        {
            DeckleLlmSource.Log.RewriteSkippedNoProvider();
            DeckleLlmSource.Log.RewriteSkippedNoProviderDetail(SentenceRewrite.Label, SentenceRewrite.ProviderId);
            return default;
        }

        try
        {
            return engine.Generate(
                SentenceRewrite.BuildRequest(sentence),
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
