using Deckle.Autocorrect;
using Deckle.Input;

namespace Deckle.Autocorrect;

// The conductor: raw key events → decode → track → decide → inject, with the
// learning signals around it. Every handler runs on the KeyboardInputHost
// input thread; Start/Stop are the only cross-thread entries.
//
// A correction is taken back only through the correction inlay (CONTEXT.md
// § Correction undo) — Backspace is always a plain Backspace. The earlier
// implicit-Backspace revert was retired after its misfires (a deleted comma
// read as an undo) wrote false suppressions (JOURNAL 2026-07-02).
//
// Gate order is doctrine (CLAUDE.md): injected events are filtered first (our
// own repairs must never feed the tracker), the password gate cuts BEFORE
// decoding (keystrokes on a password surface are never even decoded), and the
// enrollment/editability gates withhold actions without stopping observation
// resets.
public sealed class AutocorrectEngine : IDisposable
{
    private const double RollupPeriodMs = 30_000;

    private readonly IKeyboardInputHost _host;
    private readonly KeyDecoder _decoder;
    private readonly TypedWordTracker _tracker;
    private readonly ISurfaceProber _prober;
    private readonly ICorrectionPolicy _policy;
    private readonly ITextInjector _injector;
    private readonly PersonalDictionary? _dictionary;
    private readonly Func<AutocorrectSettings> _settings;
    private readonly IFrequencyLexicon? _french;
    private readonly IFrequencyLexicon? _english;

    // Opt-in per-word decision telemetry. When this returns true, each evaluated
    // word on an enrolled surface emits a structured trace (candidates, scores,
    // margins, the guard that left it literal) to the autocorrect.decisions dataset.
    // Null/false = the chain runs untouched, no trace allocated. Off by default.
    private readonly Func<bool>? _decisionTelemetry;

    // Opt-in typed-sentence corpus. When this returns true, each committed word on
    // ANY editable, non-password surface feeds the accumulator — enrollment does not
    // bound it, since the point is the user's own typing reference (auto-corrections,
    // style, rhythm), not only what the corrector touched. The accumulator emits the
    // (typed, final) sentence pair to the autocorrect.text dataset. Null = no
    // accumulator built. Off by default. The heaviest text capture — a verbatim
    // record of typed input, so it stands behind its own dedicated consent toggle.
    private readonly Func<bool>? _textTelemetry;
    private readonly SentenceCorpus? _corpus;
    private int _discardCorpusRequested;

    // The typing stream (CONTEXT.md § Typing stream) rides the same consent
    // envelope as the corpus but a TIGHTER surface gate: enrolled surfaces only,
    // where the corpus spans every editable one. Fed the same decoded keystrokes
    // the tracker consumes — a parallel verbatim capture, not a word model.
    private readonly TypingStream? _stream;

    // The post-sentence contextual stage. Null only when no sentence reranker is
    // wired; the reranker may be deterministic rules alone or rules delegating to
    // an ONNX model. The lane owns the background inference thread; the
    // coordinator owns the sentence model.
    private readonly SentenceRerankCoordinator? _coordinator;
    private readonly IRerankLane? _lane;

    private volatile FocusedSurface _surface = FocusedSurface.Unknown;

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
    // Words the user reopened and retyped after they committed — the personal
    // Words-Modified-Ratio numerator (WMR = re_edited / commits), the fate signal
    // that a committed word (corrected or not) did not stand.
    private int _rollupReEdited;
    private int _rollupLearning;
    private int _rollupGated;

    /// <summary>Raised on the input thread when the focused surface changes (surface, enrolled).</summary>
    public event Action<FocusedSurface, bool>? SurfaceChanged;

    /// <summary>Raised on the input thread after a correction burst landed.</summary>
    public event Action<CorrectionDecision>? CorrectionApplied;

    /// <summary>
    /// Raised on the input thread when an injection burst did not land
    /// (original, replacement) — UIPI-blocked elevated target, partial send.
    /// The screen may hold anything between the two forms.
    /// </summary>
    public event Action<string, string>? InjectionFailed;

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
        IFrequencyLexicon? french = null,
        IFrequencyLexicon? english = null,
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
        _stream = textTelemetry is null ? null : new TypingStream { Completed = EmitStreamRun };

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
        _host.DrainRequested += OnDrainRequested;
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
        _corpus?.Discard();
        _stream?.Discard();
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
        _host.DrainRequested -= OnDrainRequested;
        _tracker.WordCommitted -= OnWordCommitted;
        _tracker.WordEdited -= OnWordEdited;
        _tracker.TrackerReset -= OnTrackerReset;
    }

    // ── Input thread handlers ────────────────────────────────────────────

    // Called by the host when telemetry settings change. RequestDrain is the
    // cross-thread marshalling point; the corpus itself remains input-thread-owned.
    public void ReconcileTextTelemetry()
    {
        if (_textTelemetry?.Invoke() == true) return;
        Interlocked.Exchange(ref _discardCorpusRequested, 1);
        _host.RequestDrain();
    }

    private void OnDrainRequested()
    {
        if (Interlocked.Exchange(ref _discardCorpusRequested, 0) != 0)
        {
            _corpus?.Discard();
            _stream?.Discard();
        }
    }

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

        // The typing stream captures the verbatim stroke before the tracker
        // interprets it. Gated per stroke — enrolled surfaces only, consent live;
        // the password gate already cut above, before decoding.
        if (ShouldFeedStream())
            _stream?.OnKeystroke(k);

        _tracker.OnKeystroke(k);
    }

    private void OnPointerInteraction()
    {
        _tracker.NotifyPointerInteraction();
        // Ungated: a span must close on every caret move, or a stale run would
        // leak into whatever surface is typed next.
        _stream?.NotifyPointerInteraction();
    }

    // The tracker reset (Enter, focus, pointer, navigation, …) clears the sentence
    // model. Enter is forwarded verbatim so the coordinator can vouch the next word
    // as sentence-initial; every other reason is a caret move to an unknown spot.
    private void OnTrackerReset(ResetReason reason, bool droppedPartialWord)
    {
        // Close the corpus sentence first (Enter emits it tagged "enter", any other
        // reason emits the partial run tagged "interrupted" — still verbatim keyboard
        // input); the emit is gated downstream by the sink, so a flip to off between
        // accumulation and reset cannot leak a sentence to disk.
        _corpus?.Reset(reason);
        // A reset that threw away a word in flight can leave its tail to commit as
        // the next "word" — a fragment that used to pollute corpus sentence starts
        // (« e Setting UX … »). The corpus holds that next word suspect and drops it.
        if (droppedPartialWord)
            _corpus?.MarkNextWordSuspect();
        _coordinator?.Invalidate(reason);
    }

    // A correction the contextual stage applied behind the caret. It counts and
    // logs like any correction, and records a Sentence transition on the corpus
    // slot if the sentence is still open (a rewrite after flush is invisible).
    private void OnCoordinatorApplied(CorrectionDecision decision)
    {
        _rollupCorrections++;
        DeckleAutocorrectSource.Log.CorrectionApplied();
        DeckleAutocorrectSource.Log.CorrectionDetail(
            decision.Reason.ToString(), decision.Original.Length, decision.Replacement.Length, 0);
        if (_textTelemetry?.Invoke() == true)
            _corpus?.SentenceEdit(decision.Original, decision.Replacement);
        CorrectionApplied?.Invoke(decision);
    }

    private void OnFocusChanged()
    {
        var surface = _prober.Probe();
        // The reset synchronously closes the corpus run and the typing-stream
        // span. Keep the producing surface live until both have been emitted;
        // only then publish the newly focused surface.
        _tracker.NotifyFocusChanged();
        _stream?.NotifyFocusChanged();
        _surface = surface;

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

        // A declined app (decided, off) is left alone for CORRECTION — no policy
        // run, no suggestion — but the text corpus still records what was typed:
        // enrollment no longer bounds collection. Only an enabled or a not-yet-
        // decided app is evaluated by the policy below.
        if (!enabledHere && !undecided)
        {
            FeedCorpus(commit, commit.Word); // on-screen is the verbatim typed word
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

        // A word the user reopened and retyped is exempt from the commit stage:
        // the deliberate keystroke asserts intent, so the literal stands here and
        // no decision is computed. The sentence stage keeps its full rights below.
        // The decision ledger is otherwise built only on an enrolled surface and
        // only when the opt-in toggle is on — null, and the chain runs at no cost.
        CorrectionTrace? trace = !commit.Reopened && enabledHere && _decisionTelemetry?.Invoke() == true
            ? new CorrectionTrace()
            : null;
        var decision = commit.Reopened ? null : _policy.Evaluate(commit.Word, leftContext, trace);

        // A suppressed pair never fires again whatever the policy says — enforced
        // here so even a policy without dictionary access honors the suppression.
        if (decision is not null
            && _dictionary?.IsSuppressed(decision.Original, decision.Replacement) == true)
        {
            decision = null;
            trace?.MarkSuppressed(); // a stage fired, a learned suppression vetoed it
        }

        if (enabledHere)
        {
            long wordId = ++_wordId;

            // Learning feeds on words the engine leaves alone. A corrected commit
            // must NOT reinforce the bare typo, or a few repetitions would adopt
            // it and silently disable its own correction. A reopened word reaches
            // this branch too (its literal stands): the retype is a fresh clean
            // occurrence, while OnWordEdited debits the form it reopened.
            if (decision is null)
                RecordCommitLearning(commit.Word);
            else
                ApplyCorrection(commit, decision);

            if (trace is not null)
                EmitDecision(wordId, commit.Word, leftContext, trace);

            // Feed the typed-sentence corpus the (typed, on-screen) pair — the
            // decision replacement when one applied, else the typed word itself.
            FeedCorpus(commit, decision?.Replacement ?? commit.Word);

            // Feed the contextual stage both the typed literal and the on-screen
            // form. It may weigh literals the commit stage left alone, and may
            // take back diacritics corrections from full sentence context; typo,
            // elision and grammar edits stay outside its rights.
            _coordinator?.OnWordCommitted(
                commit.Word, decision?.Replacement ?? commit.Word, commit.Boundary,
                sentenceMayEvaluate: SentenceStageMayEvaluate(decision), wordId);
        }
        else
        {
            // Not yet decided: never correct here. A correction that WOULD have
            // applied is the trigger to offer enrollment for this app — once.
            if (decision is not null)
                MaybeSuggestEnrollment(surface.ProcessName);
            // Correction withheld, so the on-screen form is the typed word itself.
            FeedCorpus(commit, commit.Word);
            _rollupGated++;
            _coordinator?.Invalidate(ResetReason.FocusChanged);
        }

        MaybeRollup(commit.TimestampMs);
    }

    private void ApplyCorrection(WordCommit commit, CorrectionDecision decision)
    {
        // The boundary as the screen shows it: an elision commit carries its
        // apostrophe inside the word, so rendering the boundary again would
        // overstate the screen by one char and desync the injection.
        string boundary = WordBoundaries.DisplaySeparator(commit.Boundary);
        string current = decision.Original + boundary;
        string target = decision.Replacement + boundary;
        var plan = InjectionPlan.Compute(current, target);

        if (_injector.Replace(current, target))
        {
            _tracker.ReplaceLastCommitted(decision.Replacement);
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
            InjectionFailed?.Invoke(decision.Original, decision.Replacement);
        }
    }

    // Feed the typed-sentence corpus one word: the verbatim typed form paired with
    // the form the engine left on screen (onScreen). Collection reaches here only for
    // editable, non-password, master-on commits — the two early gates already withheld
    // the rest — and spans every such surface regardless of enrollment. The dedicated
    // consent toggle is the sole remaining gate, and exactly one call fires per commit.
    // commit.TimestampMs feeds the corpus rhythm (cast to whole ms; 0 stays unknown).
    private void FeedCorpus(WordCommit commit, string onScreen)
    {
        if (_textTelemetry?.Invoke() == true)
            _corpus?.Word(commit.Word, onScreen, commit.Boundary, (long)commit.TimestampMs);
    }

    // Emits one completed corpus sentence on the dedicated dataset, tagged with the
    // current process, its closure (how the run ended) and its per-slot timing. Runs
    // on the input thread (the accumulator is synchronous), so _surface is the live
    // surface that produced the sentence.
    private void EmitText(SentenceCorpus.SentenceRecord rec)
    {
        // Consent is live, not captured when the sentence starts. A reset can
        // close an accumulated run after the user has switched collection off;
        // do not expose that verbatim text to any EventSource listener.
        if (_textTelemetry?.Invoke() != true)
            return;

        DeckleAutocorrectSource.Log.AutocorrectTextRecorded(
            _surface.ProcessName, rec.Typed, rec.Final, rec.History, rec.Closure, rec.Timing);
    }

    // The typing stream records only where correction is live: an editable,
    // non-password (cut upstream), master-on, ENROLLED surface with the text
    // consent on — tighter than the corpus, which spans every editable surface.
    // Checked per stroke so a settings flip takes effect immediately; resets
    // (pointer, focus) bypass this gate so a span can never straddle surfaces.
    private bool ShouldFeedStream()
    {
        if (_stream is null || !_surface.IsTextEditable) return false;
        var settings = _settings();
        return settings.Enabled
            && IsEnabledFor(settings, _surface.ProcessName)
            && _textTelemetry?.Invoke() == true;
    }

    // Emits one closed typing-stream run on the dedicated dataset, tagged with
    // the producing process. Runs on the input thread; consent is re-checked at
    // emission for the same reason as EmitText — a reset can close a span after
    // the user switched collection off.
    private void EmitStreamRun(TypingStream.RunRecord rec)
    {
        if (_textTelemetry?.Invoke() != true)
            return;

        DeckleAutocorrectSource.Log.AutocorrectStreamRecorded(
            _surface.ProcessName, rec.Text, rec.Erased, rec.Closure, rec.Timing);
    }

    // The synchronous decision line of the per-word telemetry: the word, its left
    // context, the outcome, the decisive stage/reason, that stage's candidate pool
    // and safety gauges, and the full per-stage trail. The deferred reranker verdict
    // (when the word becomes an ambiguous slot) joins it later on the same id.
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
        if (!settings.Enabled || !surface.IsTextEditable || surface.IsPassword)
            return;

        // Corpus collection spans every consented editable surface. Enrollment
        // gates correction and learning, not reconstruction of the text already
        // fed by OnWordCommitted.
        if (_textTelemetry?.Invoke() == true)
            _corpus?.Edit(edit.Original, edit.Replacement);

        if (!IsEnabledFor(settings, surface.ProcessName))
            return;

        // A committed word the user reopened and retyped — the WMR signal, counted
        // whatever the retype was (a hand-fix, a rewording, an undo of a correction).
        _rollupReEdited++;

        // The word was reopened after commit: that occurrence is no longer
        // clean enough for personal-vocabulary adoption.
        string o = edit.Original, r = edit.Replacement;
        _dictionary?.RecordReEdit(o);

        // « typed bare, went back, fixed the accents by hand » — useful pair
        // evidence, but not a clean verbatim adoption occurrence.
        if (!string.Equals(o, r, StringComparison.Ordinal)
            && AccentFolding.Fold(o) == AccentFolding.Fold(r)
            && AccentFolding.HasDiacritics(r)
            && !AccentFolding.HasDiacritics(o))
        {
            _dictionary?.RecordManualAccentFix(o.ToLowerInvariant(), r.ToLowerInvariant());
            _rollupLearning++;
            DeckleAutocorrectSource.Log.LearningSignal("manual_accent_fix");
        }

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
        if (_english?.Contains(lower) == true) return;

        _dictionary.RecordCommit(word);
        _rollupLearning++;
        DeckleAutocorrectSource.Log.LearningSignal("commit");
    }

    private static bool SentenceStageMayEvaluate(CorrectionDecision? decision) =>
        decision is null
        || decision.Reason is CorrectionReason.LexicalGate
            or CorrectionReason.ContextPair
            or CorrectionReason.FrequencyDominance
            or CorrectionReason.PersonalWord;

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
            _rollupCommits, _rollupCorrections, _rollupReEdited, _rollupLearning, _rollupGated);

        _rollupStartMs = nowMs;
        _rollupCommits = 0;
        _rollupCorrections = 0;
        _rollupReEdited = 0;
        _rollupLearning = 0;
        _rollupGated = 0;
    }
}
