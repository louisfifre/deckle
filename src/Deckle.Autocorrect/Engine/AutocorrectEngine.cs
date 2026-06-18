using Deckle.Autocorrect;
using Deckle.Input;

namespace Deckle.Autocorrect;

// The conductor: raw key events → decode → track → decide → inject, with the
// revert gesture and the learning signals around it. Every handler runs on the
// KeyboardInputHost input thread; Start/Stop are the only cross-thread entries.
//
// Gate order is doctrine (CLAUDE.md): injected events are filtered first (our
// own repairs must never feed the tracker), the password gate cuts BEFORE
// decoding (keystrokes on a password surface are never even decoded), and the
// enrollment/editability gates withhold actions without stopping observation
// resets.
public sealed class AutocorrectEngine : IDisposable
{
    private const double RollupPeriodMs = 30_000;

    // The revert gesture is honoured only when the Backspace lands within this
    // window of the correction. A later Backspace — the user moved on, then came
    // back to delete the sentence — is a plain edit, not an undo, and must never
    // be read as a revert: that would restore the typo AND write a false
    // « reverted » learning signal against a correction the user kept.
    private const double RevertWindowMs = 2_000;

    // Learning eligibility: a word already living in the base lexicons needs no
    // adoption; an English form above this frequency is "known English", not a
    // personal word. Calibration constant, not a setting.
    private const double EnglishKnownPerMillion = 0.5;

    private readonly IKeyboardInputHost _host;
    private readonly KeyDecoder _decoder;
    private readonly TypedWordTracker _tracker;
    private readonly ISurfaceProber _prober;
    private readonly ICorrectionPolicy _policy;
    private readonly ITextInjector _injector;
    private readonly PersonalDictionary? _dictionary;
    private readonly Func<AutocorrectSettings> _settings;
    private readonly FrequencyLexicon? _french;
    private readonly FrequencyLexicon? _english;

    // Opt-in per-word decision telemetry. When this returns true, each evaluated
    // word on an enrolled surface emits a structured trace (candidates, scores,
    // margins, the guard that left it literal) to the autocorrect.decisions dataset.
    // Null/false = the chain runs untouched, no trace allocated. Off by default.
    private readonly Func<bool>? _decisionTelemetry;

    // Opt-in typed-sentence corpus. When this returns true, each committed word on
    // an enrolled surface feeds the accumulator, which emits the (typed, final)
    // sentence pair to the autocorrect.text dataset. Null = no accumulator built.
    // Off by default. The heaviest text capture — a verbatim record of typed input.
    private readonly Func<bool>? _textTelemetry;
    private readonly SentenceCorpus? _corpus;

    // The post-sentence contextual stage. Null when no reranker model is present
    // — the engine then runs exactly as before, gate + typo only. The lane owns
    // the background inference thread; the coordinator owns the sentence model.
    private readonly SentenceRerankCoordinator? _coordinator;
    private readonly IRerankLane? _lane;

    private volatile FocusedSurface _surface = FocusedSurface.Unknown;

    // Armed after a correction lands, stamped with the commit time; the very next
    // physical keystroke either triggers the revert (a Backspace within
    // RevertWindowMs) or disarms it. The « come back later » variant of the revert
    // needs caret-position knowledge v1 does not have.
    private (string Original, string Replacement, double ArmedAtMs)? _revertArmed;

    // Apps already offered for enrollment this run — a would-be correction on an
    // undecided surface prompts once, then stays silent until the user answers.
    private readonly HashSet<string> _suggested = new(StringComparer.OrdinalIgnoreCase);

    // Monotonic per-word id, input thread only. Stamps each evaluated word so its
    // synchronous decision line and the deferred reranker verdict join on one id.
    private long _wordId;

    // Rollup accumulators — input thread only.
    private double _rollupStartMs = -1;
    private int _rollupCommits;
    private int _rollupCorrections;
    private int _rollupReverts;
    private int _rollupLearning;
    private int _rollupGated;

    /// <summary>Raised on the input thread when the focused surface changes (surface, enrolled).</summary>
    public event Action<FocusedSurface, bool>? SurfaceChanged;

    /// <summary>Raised on the input thread after a correction burst landed.</summary>
    public event Action<CorrectionDecision>? CorrectionApplied;

    /// <summary>Raised on the input thread after a revert restored the literal (original, replacement).</summary>
    public event Action<string, string>? CorrectionReverted;

    /// <summary>
    /// Raised on the input thread when an injection burst did not land
    /// (original, replacement, isRevert) — UIPI-blocked elevated target,
    /// partial send. The screen may hold anything between the two forms.
    /// </summary>
    public event Action<string, string, bool>? InjectionFailed;

    /// <summary>
    /// Raised on the input thread when a correction would have applied on an
    /// editable, non-password, not-yet-decided surface — the signal to offer
    /// enrollment for that process. Fires at most once per process per run.
    /// </summary>
    public event Action<string>? EnrollmentSuggested;

    public FocusedSurface CurrentSurface => _surface;

    public AutocorrectEngine(
        IKeyboardInputHost host,
        KeyDecoder decoder,
        TypedWordTracker tracker,
        ISurfaceProber prober,
        ICorrectionPolicy policy,
        ITextInjector injector,
        Func<AutocorrectSettings> settings,
        PersonalDictionary? dictionary = null,
        FrequencyLexicon? french = null,
        FrequencyLexicon? english = null,
        ISentenceReranker? reranker = null,
        IAmbiguityProbe? probe = null,
        Func<bool>? decisionTelemetry = null,
        Func<bool>? textTelemetry = null)
    {
        _host = host;
        _decoder = decoder;
        _tracker = tracker;
        _prober = prober;
        _policy = policy;
        _injector = injector;
        _settings = settings;
        _dictionary = dictionary;
        _french = french;
        _english = english;
        _decisionTelemetry = decisionTelemetry;
        _textTelemetry = textTelemetry;
        _corpus = textTelemetry is null ? null : new SentenceCorpus { Completed = EmitText };

        // The contextual stage exists only with both a model and a probe. The lane
        // marshals inference off this thread and the verdict back via the host pump.
        if (reranker is not null && probe is not null)
        {
            _lane = new BackgroundRerankLane(reranker, host);
            _coordinator = new SentenceRerankCoordinator(
                lane: _lane,
                probe: probe,
                injector: injector,
                currentPartial: () => tracker.CurrentWord,
                realignLastCommitted: tracker.ReplaceLastCommitted,
                onApplied: OnCoordinatorApplied,
                decisionTelemetry: decisionTelemetry);
        }
    }

    public bool Start()
    {
        _host.KeyReceived += OnKey;
        _host.PointerInteraction += OnPointerInteraction;
        _host.FocusChanged += OnFocusChanged;
        _tracker.WordCommitted += OnWordCommitted;
        _tracker.WordEdited += OnWordEdited;
        _tracker.TrackerReset += OnTrackerReset;

        if (!_host.Start())
        {
            Unsubscribe();
            return false;
        }

        OnFocusChanged(); // seed the surface before the first focus event
        DeckleAutocorrectSource.Log.EngineStarted();
        return true;
    }

    public void Stop()
    {
        _host.Stop();
        Unsubscribe();
        _coordinator?.Invalidate(ResetReason.FocusChanged); // drop the sentence model
        _dictionary?.Flush();
        DeckleAutocorrectSource.Log.EngineStopped();
    }

    // Permanent teardown: stop, then tear down the rerank lane (joins its worker
    // and releases the model). Stop alone is reused across enable/disable cycles.
    public void Dispose()
    {
        Stop();
        _coordinator?.Dispose();
        _lane?.Dispose();
    }

    private void Unsubscribe()
    {
        _host.KeyReceived -= OnKey;
        _host.PointerInteraction -= OnPointerInteraction;
        _host.FocusChanged -= OnFocusChanged;
        _tracker.WordCommitted -= OnWordCommitted;
        _tracker.WordEdited -= OnWordEdited;
        _tracker.TrackerReset -= OnTrackerReset;
    }

    // ── Input thread handlers ────────────────────────────────────────────

    private void OnKey(KeyboardKeyEvent e)
    {
        if (e.IsInjected) return;            // our own repairs never feed the view
        if (_surface.IsPassword) return;     // hard gate — before decoding

        var stroke = _decoder.Decode(e);
        if (stroke is null) return;
        var k = stroke.Value;

        // The coordinator sees the live word as it stood BEFORE the tracker
        // consumes this stroke — so a Backspace into committed text invalidates its
        // model. Resets proper arrive via OnTrackerReset.
        _coordinator?.NotePhysicalKey(k, _tracker.CurrentWord);

        if (_revertArmed is { } armed)
        {
            _revertArmed = null;
            // Only an immediate Backspace is an undo. Past the window it is a
            // plain delete — disarm and let it flow to the tracker untouched.
            if (k.Kind == KeystrokeKind.Backspace
                && k.TimestampMs - armed.ArmedAtMs <= RevertWindowMs)
            {
                HandleRevert((armed.Original, armed.Replacement), k);
                return;
            }
        }

        _tracker.OnKeystroke(k);
    }

    // CONTEXT.md correction revert: the Backspace that deletes the boundary
    // sitting right after a corrected word also restores the original word.
    private void HandleRevert((string Original, string Replacement) armed, Keystroke backspace)
    {
        _tracker.OnKeystroke(backspace); // tracker re-opens the replacement

        if (_injector.Replace(armed.Replacement, armed.Original))
        {
            _tracker.ReplaceReopenedBuffer(armed.Original);
            _dictionary?.RecordRevert(
                armed.Original.ToLowerInvariant(), armed.Replacement.ToLowerInvariant());
            _rollupReverts++;
            DeckleAutocorrectSource.Log.CorrectionReverted();
            CorrectionReverted?.Invoke(armed.Original, armed.Replacement);
        }
        else
        {
            // The boundary is already gone (physical Backspace) and the word
            // still shows corrected: leave a trace, the tracker re-opened the
            // ORIGINAL and now disagrees with the screen.
            var plan = InjectionPlan.Compute(armed.Replacement, armed.Original);
            DeckleAutocorrectSource.Log.InjectionFailed(plan.Backspaces, plan.Text.Length);
            InjectionFailed?.Invoke(armed.Original, armed.Replacement, true);
        }
    }

    private void OnPointerInteraction()
    {
        _revertArmed = null;
        _tracker.NotifyPointerInteraction();
    }

    // The tracker reset (Enter, focus, pointer, navigation, …) clears the sentence
    // model. Enter is forwarded verbatim so the coordinator can vouch the next word
    // as sentence-initial; every other reason is a caret move to an unknown spot.
    private void OnTrackerReset(ResetReason reason)
    {
        // Close the corpus sentence first (Enter emits it, any other reason drops
        // the partial run); the emit is gated downstream by the sink, so a flip to
        // off between accumulation and reset cannot leak a sentence to disk.
        _corpus?.Reset(reason);
        _coordinator?.Invalidate(reason);
    }

    // A correction the contextual stage applied behind the caret. It is NOT
    // revertable by the one-Backspace gesture (the slot is no longer adjacent), so
    // it never arms _revertArmed; it only counts and logs like any correction.
    private void OnCoordinatorApplied(CorrectionDecision decision)
    {
        _rollupCorrections++;
        DeckleAutocorrectSource.Log.CorrectionApplied();
        DeckleAutocorrectSource.Log.CorrectionDetail(
            decision.Reason.ToString(), decision.Original.Length, decision.Replacement.Length, 0);
        CorrectionApplied?.Invoke(decision);
    }

    private void OnFocusChanged()
    {
        _revertArmed = null;
        var surface = _prober.Probe();
        _surface = surface;
        _tracker.NotifyFocusChanged();

        bool enabled = IsEnabledFor(_settings(), surface.ProcessName);
        DeckleAutocorrectSource.Log.SurfaceChanged(
            surface.ProcessName, surface.IsTextEditable, surface.IsPassword, enabled, surface.Probe);
        SurfaceChanged?.Invoke(surface, enabled);
    }

    private void OnWordCommitted(WordCommit commit)
    {
        _rollupCommits++;

        var surface = _surface;
        var settings = _settings();

        // Editability, password and the master switch withhold ALL action — and
        // the policy itself — without stopping observation resets.
        if (!settings.Enabled || !surface.IsTextEditable || surface.IsPassword)
        {
            _rollupGated++;
            _coordinator?.Invalidate(ResetReason.PasswordSurface);
            MaybeRollup(commit.TimestampMs);
            return;
        }

        bool enabledHere = IsEnabledFor(settings, surface.ProcessName);
        bool undecided = !IsDecided(settings, surface.ProcessName);

        // A declined app (decided, off) is left entirely alone — no policy run,
        // no suggestion. Only an enabled or a not-yet-decided app is evaluated.
        if (!enabledHere && !undecided)
        {
            _rollupGated++;
            _coordinator?.Invalidate(ResetReason.FocusChanged);
            MaybeRollup(commit.TimestampMs);
            return;
        }

        // Live path: up to two words of left context, most recent last. The
        // disambiguator is an n-gram with backoff — it uses the trigram row when
        // both words are present, falling back to bigram then unigram on its own.
        var leftContext = commit.PreviousWord is null
            ? Array.Empty<string>()
            : commit.PreviousPreviousWord is null
                ? new[] { commit.PreviousWord }
                : new[] { commit.PreviousPreviousWord, commit.PreviousWord };

        // The decision ledger is built only on an enrolled surface and only when
        // the opt-in toggle is on — otherwise null, and the chain runs at no cost.
        CorrectionTrace? trace = enabledHere && _decisionTelemetry?.Invoke() == true
            ? new CorrectionTrace()
            : null;
        var decision = _policy.Evaluate(commit.Word, leftContext, trace);

        // A reverted pair stays suppressed whatever the policy says — enforced
        // here so even a policy without dictionary access honors the gesture.
        if (decision is not null
            && _dictionary?.IsSuppressed(decision.Original, decision.Replacement) == true)
        {
            decision = null;
            trace?.MarkSuppressed(); // a stage fired, a learned revert vetoed it
        }

        if (enabledHere)
        {
            long wordId = ++_wordId;

            // Learning feeds on words the engine leaves alone. A corrected commit
            // must NOT reinforce the bare typo, or a few repetitions would adopt
            // it and silently disable its own correction.
            if (decision is null)
                RecordCommitLearning(commit.Word);
            else
                ApplyCorrection(commit, decision);

            if (trace is not null)
                EmitDecision(wordId, commit.Word, leftContext, trace);

            // Feed the typed-sentence corpus the (typed, on-screen) pair. Gated on
            // the dedicated toggle, so nothing accumulates when it is off.
            if (_textTelemetry?.Invoke() == true)
                _corpus?.Word(commit.Word, decision?.Replacement ?? commit.Word, commit.Boundary);

            // Feed the contextual stage the on-screen form (post-gate). A null
            // decision means the gate left a literal — the only case a real-word
            // ambiguity survives for the reranker.
            _coordinator?.OnWordCommitted(
                decision?.Replacement ?? commit.Word, commit.Boundary,
                gateLeftLiteral: decision is null, wordId);
        }
        else
        {
            // Not yet decided: never correct here. A correction that WOULD have
            // applied is the trigger to offer enrollment for this app — once.
            if (decision is not null)
                MaybeSuggestEnrollment(surface.ProcessName);
            _rollupGated++;
            _coordinator?.Invalidate(ResetReason.FocusChanged);
        }

        MaybeRollup(commit.TimestampMs);
    }

    private void ApplyCorrection(WordCommit commit, CorrectionDecision decision)
    {
        string boundary = commit.Boundary.ToString();
        string current = decision.Original + boundary;
        string target = decision.Replacement + boundary;
        var plan = InjectionPlan.Compute(current, target);

        if (_injector.Replace(current, target))
        {
            _tracker.ReplaceLastCommitted(decision.Replacement);
            _revertArmed = (decision.Original, decision.Replacement, commit.TimestampMs);
            _rollupCorrections++;
            DeckleAutocorrectSource.Log.CorrectionApplied();
            DeckleAutocorrectSource.Log.CorrectionDetail(
                decision.Reason.ToString(), decision.Original.Length,
                decision.Replacement.Length, plan.Backspaces);
            CorrectionApplied?.Invoke(decision);
        }
        else
        {
            DeckleAutocorrectSource.Log.InjectionFailed(plan.Backspaces, plan.Text.Length);
            InjectionFailed?.Invoke(decision.Original, decision.Replacement, false);
        }
    }

    // The synchronous decision line of the per-word telemetry: the word, its left
    // context, the outcome, the decisive stage/reason, that stage's candidate pool
    // and safety gauges, and the full per-stage trail. The deferred reranker verdict
    // (when the word becomes an ambiguous slot) joins it later on the same id.
    // Emits one completed corpus sentence on the dedicated dataset, tagged with the
    // current process. Runs on the input thread (the accumulator is synchronous), so
    // _surface is the live surface that produced the sentence.
    private void EmitText(string typed, string final)
    {
        DeckleAutocorrectSource.Log.AutocorrectTextRecorded(_surface.ProcessName, typed, final);
    }

    private static void EmitDecision(long id, string word, IReadOnlyList<string> leftContext, CorrectionTrace trace)
    {
        DeckleAutocorrectSource.Log.AutocorrectDecisionRecorded(
            id,
            word,
            string.Join(' ', leftContext),
            trace.Outcome,
            trace.PrimaryStage,
            trace.PrimaryReason,
            trace.RenderCandidates(),
            trace.RenderGauges(),
            trace.RenderTrail());
    }

    private void OnWordEdited(WordEdit edit)
    {
        var surface = _surface;
        var settings = _settings();
        if (!settings.Enabled || !IsEnabledFor(settings, surface.ProcessName)
            || !surface.IsTextEditable || surface.IsPassword)
            return;

        // « typed bare, went back, fixed the accents by hand » — the strongest
        // organic signal that the accented form is the wanted one.
        string o = edit.Original, r = edit.Replacement;
        if (!string.Equals(o, r, StringComparison.Ordinal)
            && AccentFolding.Fold(o) == AccentFolding.Fold(r)
            && AccentFolding.HasDiacritics(r)
            && !AccentFolding.HasDiacritics(o))
        {
            _dictionary?.RecordManualAccentFix(o.ToLowerInvariant(), r.ToLowerInvariant());
            _rollupLearning++;
            DeckleAutocorrectSource.Log.LearningSignal("manual_accent_fix");
        }

        // Fold the hand-fix into the corpus sentence: the typed side keeps the
        // first attempt, the final side takes the retype.
        if (_textTelemetry?.Invoke() == true)
            _corpus?.Edit(edit.Original, edit.Replacement);
    }

    // Commits feed adoption only for plain out-of-lexicon words — the base
    // lexicons need no reinforcement, and content guards back the password
    // gate (no digits, sane length).
    private void RecordCommitLearning(string word)
    {
        if (_dictionary is null) return;
        if (word.Length < 2 || word.Length > 30) return;
        if (word.EndsWith('\'')) return;
        foreach (char c in word)
            if (!char.IsLetter(c) && c != '-' && c != '\'') return;

        string lower = word.ToLowerInvariant();
        if (_french?.Contains(lower) == true) return;
        if (_english?.FrequencyOf(lower) >= EnglishKnownPerMillion) return;

        _dictionary.RecordCommit(lower);
        _rollupLearning++;
        DeckleAutocorrectSource.Log.LearningSignal("commit");
    }

    // Corrections run here only when the app's decision is explicitly on.
    private static bool IsEnabledFor(AutocorrectSettings settings, string processName)
        => processName.Length > 0
        && settings.Apps.TryGetValue(processName, out bool on) && on;

    // The user has answered for this app (on or off) — absent means never met.
    private static bool IsDecided(AutocorrectSettings settings, string processName)
        => processName.Length > 0 && settings.Apps.ContainsKey(processName);

    // First would-be correction on a not-yet-decided app raises the enrollment
    // offer; the per-run guard keeps it to a single prompt until the user answers.
    private void MaybeSuggestEnrollment(string processName)
    {
        if (processName.Length == 0 || !_suggested.Add(processName)) return;
        DeckleAutocorrectSource.Log.EnrollmentSuggested(processName);
        EnrollmentSuggested?.Invoke(processName);
    }

    private void MaybeRollup(double nowMs)
    {
        if (_rollupStartMs < 0) _rollupStartMs = nowMs;
        if (nowMs - _rollupStartMs < RollupPeriodMs) return;

        DeckleAutocorrectSource.Log.ActivityRollup(
            _rollupCommits, _rollupCorrections, _rollupReverts, _rollupLearning, _rollupGated);

        _rollupStartMs = nowMs;
        _rollupCommits = 0;
        _rollupCorrections = 0;
        _rollupReverts = 0;
        _rollupLearning = 0;
        _rollupGated = 0;
    }
}
