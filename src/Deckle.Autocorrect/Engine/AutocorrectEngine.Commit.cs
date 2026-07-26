using Deckle.Autocorrect;
using Deckle.Diagnostics;
using Deckle.Input;
using System.Diagnostics.Tracing;

namespace Deckle.Autocorrect;

public sealed partial class AutocorrectEngine
{
    private void OnWordCommitted(WordCommit commit)
    {
        bool rollupEnabled = IsActivityRollupEnabled();
        if (rollupEnabled) _rollupCommits++;

        var surface = _surface;
        var settings = _settings();

        // Editability, password and the master switch withhold ALL action — and
        // the policy itself — without stopping observation resets.
        if (!settings.Enabled || !surface.IsTextEditable || surface.IsPassword)
        {
            if (rollupEnabled) _rollupGated++;
            _coordinator?.Invalidate(ResetReason.PasswordSurface);
            MaybeRollup(commit.TimestampMs, rollupEnabled);
            return;
        }

        bool enabledHere = IsEnabledFor(settings, surface.ProcessName);
        bool undecided = !IsDecided(settings, surface.ProcessName);

        // A declined app (decided, off) is left entirely alone: no policy run,
        // no suggestion, and no text collection. Only an enabled or a not-yet-
        // decided app is evaluated by the policy below.
        if (!enabledHere && !undecided)
        {
            FeedCorpus(commit, commit.Word); // on-screen is the verbatim typed word
            if (rollupEnabled) _rollupGated++;
            _coordinator?.Invalidate(ResetReason.FocusChanged);
            MaybeRollup(commit.TimestampMs, rollupEnabled);
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
            bool surfaceKnown = true;
            string precedingSeparators = commit.PrecedingSeparators;

            // Learning feeds on words the engine leaves alone. A corrected commit
            // must NOT reinforce the bare typo, or a few repetitions would adopt
            // it and silently disable its own correction. A reopened word reaches
            // this branch too (its literal stands): the retype is a fresh clean
            // occurrence, while OnWordEdited debits the form it reopened.
            if (decision is null)
                RecordCommitLearning(commit.Word);
            else
                surfaceKnown = ApplyCorrection(commit, decision);

            // Boundary mistouch families act on the span BEHIND the word — a
            // territory no word-level policy sees. Only when the commit itself
            // was left alone: never two injections off one keystroke.
            if (decision is null && surfaceKnown)
            {
                surfaceKnown = TryApplyMistouchRepair(
                    commit, out string? repairedPrecedingSeparators);
                if (repairedPrecedingSeparators is not null)
                    precedingSeparators = repairedPrecedingSeparators;
            }

            if (trace is not null)
                EmitDecision(wordId, commit.Word, leftContext, trace);

            // Replace=false can mean a partial SendInput burst. The screen is
            // then unknowable: do not claim the intended replacement in the
            // corpus or sentence model, and reset every tracker-owned span before
            // accepting more input.
            if (!surfaceKnown)
            {
                InvalidateModeledSurface();
                MaybeRollup(commit.TimestampMs, rollupEnabled);
                return;
            }

            // Feed the typed-sentence corpus the (typed, on-screen) pair — the
            // decision replacement when one applied, else the typed word itself.
            FeedCorpus(commit, decision?.Replacement ?? commit.Word);

            // Feed the contextual stage both the typed literal and the on-screen
            // form. It may weigh literals the commit stage left alone, and may
            // take back diacritics corrections from full sentence context; typo,
            // elision and grammar edits stay outside its rights.
            _coordinator?.OnWordCommitted(
                commit.Word, decision?.Replacement ?? commit.Word, commit.Boundary,
                sentenceMayEvaluate: SentenceStageMayEvaluate(decision), wordId,
                precedingSeparators: precedingSeparators);
        }
        else
        {
            // Not yet decided: never correct here. A correction that WOULD have
            // applied is the trigger to offer enrollment for this app — once.
            if (decision is not null)
                MaybeSuggestEnrollment(surface.ProcessName);
            // Correction withheld, so the on-screen form is the typed word itself.
            FeedCorpus(commit, commit.Word);
            if (rollupEnabled) _rollupGated++;
            _coordinator?.Invalidate(ResetReason.FocusChanged);
        }

        MaybeRollup(commit.TimestampMs, rollupEnabled);
    }

    private bool ApplyCorrection(WordCommit commit, CorrectionDecision decision)
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
            OnInjectionSucceeded();
            _tracker.ReplaceLastCommitted(decision.Replacement);
            if (IsActivityRollupEnabled()) _rollupCorrections++;
            DeckleAutocorrectSource.Log.CorrectionApplied();
            LogCorrectionDetail(decision, plan.Backspaces);
            CorrectionApplied?.Invoke(decision);
            return true;
        }

        OnInjectionFailed(plan.Backspaces, plan.Text.Length);
        InjectionFailed?.Invoke(decision.Original, decision.Replacement);
        return false;
    }

    // Applies an approved mistouch family's span repair — the separator run
    // between the previous word and the one just committed, rewritten on screen
    // (« qu;il » → « qu'il »). A learned suppression vetoes it like any
    // correction; the corpus records the separator change on the final side so
    // the emitted sentence stays glued to the screen (the typed side keeps the
    // faulty run — the mining pair). The repaired separator is returned to the
    // sentence coordinator so any deferred tail rewrite stays screen-exact.
    private bool TryApplyMistouchRepair(
        WordCommit commit,
        out string? repairedPrecedingSeparators)
    {
        repairedPrecedingSeparators = null;
        if (_mistouch is null) return true;
        MistouchFamilyCorrector.SpanRepair? repair = _mistouch.Evaluate(commit);
        if (repair is null) return true;
        if (_dictionary?.IsSuppressed(repair.Original, repair.Replacement) == true) return true;

        string boundary = WordBoundaries.DisplaySeparator(commit.Boundary);
        string current = repair.Original + boundary;
        string target = repair.Replacement + boundary;
        var plan = InjectionPlan.Compute(current, target);
        var decision = new CorrectionDecision(
            repair.Original, repair.Replacement, CorrectionReason.MistouchFamily);

        if (_injector.Replace(current, target))
        {
            OnInjectionSucceeded();
            if (IsActivityRollupEnabled()) _rollupCorrections++;
            DeckleAutocorrectSource.Log.CorrectionApplied();
            LogCorrectionDetail(decision, plan.Backspaces);
            if (CanCollectText())
                _corpus?.SeparatorEdit(repair.Previous, repair.OldSeparators, repair.NewSeparators);
            repairedPrecedingSeparators = repair.NewSeparators;
            CorrectionApplied?.Invoke(decision);
            return true;
        }

        OnInjectionFailed(plan.Backspaces, plan.Text.Length);
        InjectionFailed?.Invoke(repair.Original, repair.Replacement);
        return false;
    }

    private void OnInjectionFailed(int backspaces, int textLength)
    {
        _injectionFailures++;
        _lastInjectionBackspaces = backspaces;
        _lastInjectionTextLength = textLength;
        if (_injectionIncidentOpen) return;

        _injectionIncidentOpen = true;
        DeckleAutocorrectSource.Log.InjectionIncident();
        DeckleAutocorrectSource.Log.InjectionEpisodeDetail(
            "opened", _injectionFailures, backspaces, textLength);
    }

    private void OnInjectionSucceeded()
    {
        if (!_injectionIncidentOpen) return;

        DeckleAutocorrectSource.Log.InjectionRecovered();
        DeckleAutocorrectSource.Log.InjectionEpisodeDetail(
            "recovered", _injectionFailures,
            _lastInjectionBackspaces, _lastInjectionTextLength);
        _injectionIncidentOpen = false;
        _injectionFailures = 0;
        _lastInjectionBackspaces = 0;
        _lastInjectionTextLength = 0;
    }

    // A word is protected when any tier the engine sees knows it — the French
    // lexicon, the restricted global-English seed, or the user's own adopted
    // vocabulary. The mistouch corrector's validity oracle.
    private bool IsProtectedWord(string lowerForm) =>
        _french?.Contains(lowerForm) == true
        || _english?.Contains(lowerForm) == true
        || _dictionary?.IsAdopted(lowerForm) == true;

    // Feed the typed-sentence corpus one word: the verbatim typed form paired with
    // the form the engine left on screen (onScreen). Collection is allowed only on
    // an enrolled, editable, non-password surface while both the module and dedicated
    // consent are on. Exactly one call fires per eligible commit.
    // commit.TimestampMs feeds the corpus rhythm (cast to whole ms; 0 stays unknown).

    private void OnWordEdited(WordEdit edit)
    {
        var surface = _surface;
        var settings = _settings();
        if (!settings.Enabled || !surface.IsTextEditable || surface.IsPassword)
            return;

        if (!IsEnabledFor(settings, surface.ProcessName))
            return;

        if (CanCollectText())
            _corpus?.Edit(edit.Original, edit.Replacement);

        // A committed word the user reopened and retyped — the WMR signal, counted
        // whatever the retype was (a hand-fix, a rewording, an undo of a correction).
        if (IsActivityRollupEnabled()) _rollupReEdited++;

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
            if (IsActivityRollupEnabled()) _rollupLearning++;
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

        if (!_dictionary.RecordCommit(word))
            return;
        if (IsActivityRollupEnabled()) _rollupLearning++;
        DeckleAutocorrectSource.Log.LearningSignal("commit");
    }

    private static bool SentenceStageMayEvaluate(CorrectionDecision? decision) =>
        decision is null
        || decision.Reason is CorrectionReason.LexicalGate
            or CorrectionReason.ContextPair
            or CorrectionReason.FrequencyDominance
            or CorrectionReason.PersonalWord;

    // Corrections run here only when the app's decision is explicitly on.
}
