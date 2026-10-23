using Deckle.Diagnostics;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// ─── Rewrite service ─────────────────────────────────────────────────────────
//
// Owns what sits above the generation seam: the profile-driven request, the
// provider lookup, the hard deadline, and the human-facing outcome of a
// failure. The overlay feedback belongs to the service, not to whichever
// engine happened to run, and so do the rewrite milestones: only the service
// sees every exit path (no engine, a throw before the request, the deadline,
// the caller's own cancellation), so every Started it writes has its end here.
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

    // The caller word every rewrite milestone carries.
    const string TranscriptCaller = "transcript";
    const string ParagraphCaller  = "paragraph";
    const string SentenceCaller   = "sentence";

    // The protocol behind a provider: a provider added in Settings carries a
    // generated id that says nothing on its own.
    static string EngineName(IGenerationEngine engine) => engine switch
    {
        OllamaEngine           => "ollama",
        OpenAiCompatibleEngine => "openai_compatible",
        ClaudeEngine           => "claude",
        _                      => engine.GetType().Name,
    };

    // Why the resolver gave no engine, as a fixed word. Without an injected
    // lookup every named provider reads as provider_missing.
    string UnresolvedReason(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return "no_provider";
        LlmProvider? provider = _findProvider(providerId);
        if (provider is null) return "provider_missing";
        if (!provider.Enabled) return "provider_disabled";
        return "unresolved";
    }

    // ── Interactive incident ────────────────────────────────────────────
    //
    // Paragraph and sentence rewrites run on every pause in typing. When the
    // server is down each one would fail, and one Warning per paragraph would
    // bury the log under a single cause. So the first failure (or
    // unresolved provider) opens one incident with a Warning, the repeats
    // stay Verbose, and the next success closes it with one Informational
    // recovery. The transcript path keeps one Warning per take: each of its
    // failures shows the user an overlay of its own.

    readonly object _incidentLock = new();
    int _interactiveFailures;

    // Counts this failure into the incident; 1 means it opens the incident.
    int CountInteractiveFailure()
    {
        lock (_incidentLock) return ++_interactiveFailures;
    }

    // Closes the incident; returns how many failures it held, 0 when none
    // was open.
    int CloseInteractiveIncident()
    {
        lock (_incidentLock)
        {
            int failures = _interactiveFailures;
            _interactiveFailures = 0;
            return failures;
        }
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
            DeckleLlmSource.Log.RewriteSkippedNoProvider(TranscriptCaller, UnresolvedReason(profile.ProviderId));
            DeckleLlmSource.Log.RewriteSkippedNoProviderDetail(profile.Name, profile.ProviderId);
            var unresolved = RewriteFailureMessage.ForUnresolvedProvider(profile, _findProvider(profile.ProviderId));
            DeckleLlmSource.Log.UserFeedbackEmitted(
                severity: 1,
                title:    unresolved.Title,
                body:     unresolved.Body,
                role:     1);
            return default;
        }

        // Started before the engine runs, so a failure the engine raises
        // before any request (a missing key) still closes a rewrite it opened.
        // One work_id pairs this start with whichever end follows: the
        // completion, the timeout or the failure.
        string workId = WorkId.Next();
        DeckleLlmSource.Log.TranscriptRewriteStarted(
            profile.ProviderId, EngineName(engine), profile.Model, workId);
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
            GenerationResult result = engine.Generate(request, cts.Token);
            sw.Stop();
            DeckleLlmSource.Log.TranscriptRewriteCompleted(profile.ProviderId, sw.ElapsedMilliseconds, workId);
            return result;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            sw.Stop();
            DeckleCancellationSource.Log.OperationCancelled(
                CancelledOperations.LlmRewrite, CancellationReasons.Timeout, (int)sw.ElapsedMilliseconds);
            DeckleLlmSource.Log.RewriteTimeout(
                TranscriptCaller, profile.ProviderId, profile.Model,
                sw.ElapsedMilliseconds, GenerationDeadline.HardCap.TotalSeconds, workId);
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
            DeckleLlmSource.Log.RewriteUnavailable(
                TranscriptCaller, profile.ProviderId, profile.Model,
                RewriteFailureMessage.ReasonOf(ex), RewriteFailureMessage.StatusOf(ex),
                ex.GetType().Name, ex.Message, workId);
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
        => RewriteInteractive(
            ParagraphCaller,
            ParagraphRewrite.ProviderId,
            ParagraphRewrite.Label,
            ParagraphRewrite.Model,
            () => ParagraphRewrite.BuildRequest(paragraph, ParagraphRewrite.Model),
            cancellationToken);

    /// <summary>Generates one minimal whole-sentence correction proposal. The
    /// result is untrusted model output: callers must gate and verify it before
    /// treating any edit as a Correction.</summary>
    public GenerationResult RewriteSentence(
        string sentence,
        CancellationToken cancellationToken)
        => RewriteInteractive(
            SentenceCaller,
            SentenceRewrite.ProviderId,
            SentenceRewrite.Label,
            SentenceRewrite.Model,
            () => SentenceRewrite.BuildRequest(sentence),
            cancellationToken);

    // The shared body of the interactive rewrites. Failures stay silent at
    // the UI boundary (an interactive suggestion is optional, the caller
    // simply offers nothing) but not in the log: see the incident above.
    // The request is built inside the try, as it was before, so a throw
    // while building it is a failure like any other.
    GenerationResult RewriteInteractive(
        string caller,
        string providerId,
        string label,
        string model,
        Func<GenerationRequest> buildRequest,
        CancellationToken cancellationToken)
    {
        // Counted for the 60 s rollup before anything can return: the log sees
        // how many paragraph and sentence rewrites ran, not one line for each.
        HotLoops.LlmInteractiveRewrite.Tick();
        IGenerationEngine? engine = _engines.Resolve(providerId);
        if (engine is null)
        {
            if (CountInteractiveFailure() == 1)
                DeckleLlmSource.Log.RewriteSkippedNoProvider(caller, UnresolvedReason(providerId));
            DeckleLlmSource.Log.RewriteSkippedNoProviderDetail(label, providerId);
            return default;
        }

        DeckleLlmSource.Log.RewriteStarted(caller, providerId, EngineName(engine), model);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            GenerationResult result = engine.Generate(buildRequest(), cancellationToken);
            sw.Stop();
            DeckleLlmSource.Log.RewriteCompleted(caller, providerId, sw.ElapsedMilliseconds);
            int failures = CloseInteractiveIncident();
            if (failures > 0)
                DeckleLlmSource.Log.RewriteRecovered(caller, providerId, failures);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The interactive deadline or a keystroke that made the request
            // stale: both are expected, so no Warning, but the rewrite this
            // method started still gets its end. A cancellation says nothing
            // about the server, so the incident stays as it was.
            sw.Stop();
            DeckleLlmSource.Log.RewriteCancelled(caller, providerId, sw.ElapsedMilliseconds);
            return default;
        }
        catch (Exception ex)
        {
            sw.Stop();
            string reason = RewriteFailureMessage.ReasonOf(ex);
            int status = RewriteFailureMessage.StatusOf(ex);
            int failures = CountInteractiveFailure();
            if (failures == 1)
                DeckleLlmSource.Log.RewriteUnavailable(
                    caller, providerId, model, reason, status, ex.GetType().Name, ex.Message, work_id: string.Empty);
            else
                DeckleLlmSource.Log.RewriteAttemptFailed(
                    caller, providerId, model, reason, status, ex.GetType().Name, ex.Message, failures);
            return default;
        }
    }
}
