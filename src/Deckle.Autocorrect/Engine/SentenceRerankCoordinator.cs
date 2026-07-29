using System.Text;

namespace Deckle.Autocorrect;

// One unit of work handed to the background reranker. The production shape is a
// literal sentence plus a closed set of one-edit SentenceCandidates; SlotIndex
// and Candidates remain only for explicit legacy slot-mode callers. Epoch makes
// a result arriving after a reset recognisably stale.
public readonly record struct RerankRequest(
    IReadOnlyList<string> Sentence,
    int SlotIndex,
    IReadOnlyList<AccentVariant> Candidates,
    int Epoch,
    VerifiedCaretSentence? VerifiedSentence = null,
    IReadOnlyList<SentenceEditCandidate>? SentenceCandidates = null);

// The reranker's verdict for one slot: the full outcome (chosen form plus the
// per-candidate scores and margin for the decision telemetry), tagged with the
// buffer index and epoch it was computed against. The candidate set includes the
// typed original when the slot came from a commit-stage diacritics correction.
public readonly record struct RerankResult(int SlotIndex, int Epoch, RerankOutcome Outcome);

// The threading boundary between the input-thread coordinator and the heavy ONNX
// inference. Submit is called on the input thread and must never block; the lane
// runs the reranker elsewhere and delivers the verdict back through ResultSink,
// which it MUST invoke on the input thread. A synchronous implementation (tests)
// may call ResultSink inline.
public interface IRerankLane : IDisposable
{
    Action<RerankResult>? ResultSink { get; set; }
    void Submit(RerankRequest request);
}

// ── SentenceRerankCoordinator ────────────────────────────────────────────────
//
// The live second stage, owning a rolling model of the sentence the user is
// typing. Everything here runs on the engine's single input thread — the buffer,
// the epoch, the in-flight flag — so it is lock-free by the same rule as the
// engine itself; the only cross-thread hop is the lane.
//
// On each committed word it records the post-gate surface form and asks the probe
// whether each slot has bounded alternatives the gate left unresolved. At a
// terminal sentence ender it submits the literal sentence and all bounded
// one-edit variants as ONE transaction. The judge may keep the literal or choose
// at most one edit; it never creates text and the coordinator never cascades
// several slot verdicts. Ordinary forward typing may continue: the exact suffix
// grows in the model and rides unchanged through the minimal rewrite. Any gesture
// that can move the caret or mutate unknown text still invalidates the request.
//
// Staleness is the danger and the guards are deliberately strict: an epoch bumped
// on every reset drops any verdict that outlived its exact screen state; each
// ready slot remembers the sentence bounds it was closed under; a failed
// (UIPI-blocked) send invalidates the model rather than continue from uncertainty.
public sealed class SentenceRerankCoordinator : IDisposable
{
    // The buffer is a rolling window; past this many words it drops to a fresh
    // sentence rather than grow unbounded.
    private const int BufferCap = 40;

    // Words on each side of the slot handed to the masked-LM. Local context is
    // what resolves la/là; a wide window only slows inference and risks >512 tokens.
    private const int ContextWindow = 12;

    // Mirrors TypedWordTracker's separator-run cap. Beyond this, punctuation
    // art is not a trustworthy word gap and the modeled suffix is abandoned.
    private const int SeparatorRunCap = 8;

    // A correct rewrite can still be too intrusive when the user has already
    // typed far beyond the resolved slot. Expire it instead of backspacing and
    // replaying an arbitrarily long visible suffix.
    private const int MaxRewriteTailChars = 256;

    // Literal plus at most this many one-edit sentences enter one global judge
    // transaction. A wider candidate surface is ambiguity, not permission to
    // fall back to a cascade of local writes.
    private const int MaxSentenceEditCandidates = 12;

    // Only terminal punctuation closes a sentence. A colon or semicolon does not
    // grant the model permission to rewrite text that the user is still composing.
    private static readonly HashSet<char> SentenceEnders = new() { '.', '!', '?', '…' };

    private readonly IRerankLane _lane;
    private readonly IAmbiguityProbe _probe;
    private readonly IAmbiguityProbe _wholeSentenceProbe;
    private readonly bool _wholeSentenceEnabled;
    private readonly ITextInjector _injector;
    private readonly Func<string> _currentPartial;
    private readonly Action<string>? _realignLastCommitted;
    private readonly Action<CorrectionDecision, InjectionPlan>? _onApplied;
    // Gate for the per-word decision telemetry: when it returns true the deferred
    // reranker verdict is emitted to the autocorrect.decisions dataset (scores and
    // margin included). Null or false = no rerank line, no rendering cost.
    private readonly Func<bool>? _decisionTelemetry;

    private readonly List<SlotEntry> _buffer = new();
    private int _epoch;
    private bool _inFlight;
    private int _inFlightEpoch = -1;
    // The ABSOLUTE buffer index of the slot currently submitted. The request
    // carries a window-relative index (for the model), so the verdict cannot be
    // trusted to identify the slot — single-flight lets us remember it here.
    private int _inFlightSlot = -1;
    private bool _inFlightWholeSentence;
    private int _inFlightContextStart = -1;
    private int _inFlightContextEnd = -1;
    private bool _wholeSentenceAttempted;
    private bool _sentenceClosed;
    private int _sentenceStartIndex;
    private VerifiedCaretSentence? _verifiedSentence;
    private bool _disposed;

    public SentenceRerankCoordinator(
        IRerankLane lane,
        IAmbiguityProbe probe,
        ITextInjector injector,
        Func<string> currentPartial,
        Action<string>? realignLastCommitted = null,
        Action<CorrectionDecision, InjectionPlan>? onApplied = null,
        Func<bool>? decisionTelemetry = null,
        IAmbiguityProbe? wholeSentenceProbe = null)
    {
        _lane = lane;
        _probe = probe;
        _wholeSentenceProbe = wholeSentenceProbe ?? probe;
        _wholeSentenceEnabled = wholeSentenceProbe is not null;
        _injector = injector;
        _currentPartial = currentPartial;
        _realignLastCommitted = realignLastCommitted;
        _onApplied = onApplied;
        _decisionTelemetry = decisionTelemetry;
        _lane.ResultSink = ApplyResult;
    }

    // ── Input-thread feed ────────────────────────────────────────────────────

    public void OnWordCommitted(string finalForm, char boundary, bool gateLeftLiteral, long wordId = 0) =>
        OnWordCommitted(finalForm, finalForm, boundary, sentenceMayEvaluate: gateLeftLiteral, wordId);

    // A word committed. typedForm is the user's actual literal; finalForm is what
    // is on screen after the commit stage. sentenceMayEvaluate is true when the
    // commit stage either stood aside or made a diacritics correction the sentence
    // stage is allowed to revise from full context; false for typo, elision and
    // grammar corrections, whose edits are outside the reranker's candidate set.
    // wordId is the engine's per-word id, carried so a deferred verdict joins its
    // synchronous decision line.
    public void OnWordCommitted(
        string typedForm,
        string finalForm,
        char boundary,
        bool sentenceMayEvaluate,
        long wordId = 0,
        string precedingSeparators = "")
    {
        if (_disposed) return;

        // Production reopens the model on the first forward word character after
        // closure. Keep this direct-call guard for callers that bypass the key feed.
        if (_sentenceClosed)
            Invalidate(ResetReason.Navigation);

        // The tracker is the authority for the exact run that separated this
        // word from the previous one (", ", " : ", doubled spaces, …). Reconcile
        // the prior slot before appending this word. Empty means unknown/direct
        // test call, in which case the keystroke feed remains our best model.
        if (_buffer.Count > 0 && precedingSeparators.Length > 0)
            _buffer[^1].Separator = precedingSeparators;

        var entry = new SlotEntry
        {
            Form = finalForm,
            Separator = WordBoundaries.DisplaySeparator(boundary),
            WordId = wordId,
        };

        if (sentenceMayEvaluate)
        {
            bool literalUntouched = string.Equals(
                typedForm, finalForm, StringComparison.Ordinal);
            IReadOnlyList<AccentVariant> candidates = literalUntouched
                ? _probe.AmbiguousCandidates(finalForm)
                : _probe.CorrectionCandidates(typedForm);
            IReadOnlyList<AccentVariant> wholeSentenceCandidates = literalUntouched
                ? _wholeSentenceProbe.AmbiguousCandidates(finalForm)
                : candidates;
            if (candidates.Count >= 2 || wholeSentenceCandidates.Count >= 2)
            {
                entry.IsAmbiguous = true;
                entry.Candidates = candidates;
                entry.WholeSentenceCandidates = wholeSentenceCandidates;
                DeckleAutocorrectSource.Log.RerankSlotPending(
                    Math.Max(candidates.Count, wholeSentenceCandidates.Count),
                    finalForm.Length);
            }
        }

        _buffer.Add(entry);

        _sentenceClosed = SentenceEnders.Contains(boundary);

        if (_buffer.Count > BufferCap)
        {
            Invalidate(ResetReason.BufferLimit);
            return;
        }

        if (_sentenceClosed)
        {
            MarkCurrentSentenceReady();
            TrySubmitNext();
        }
    }

    // Every physical keystroke, with whether the live word had content BEFORE the
    // tracker consumed it. A Backspace on an empty live buffer re-opens a committed word
    // for editing — our append-only model can no longer mirror the screen, so we
    // start fresh. (Resets proper arrive via Invalidate.)
    public void NotePhysicalKey(Keystroke k, bool hasPartialWord)
    {
        if (_disposed) return;

        // Separators typed after terminal punctuation (a space, closing quote or
        // another dot) extend the exact suffix without moving the caret away.
        // Preserve them so a verdict can still land during that natural pause.
        // Forward word characters start a new tracked sentence and do not expire
        // the old request; control/edit gestures still do.
        if (_sentenceClosed)
        {
            if (k.Kind == KeystrokeKind.Text
                && !hasPartialWord)
            {
                if (k.Text.Any(WordBoundaries.IsWordChar))
                {
                    _sentenceClosed = false;
                    _sentenceStartIndex = _buffer.Count;
                    _wholeSentenceAttempted = false;
                    return;
                }
                foreach (char c in k.Text)
                    if (!TryAppendTrailingSeparator(c))
                    {
                        Invalidate(ResetReason.Navigation);
                        return;
                    }
                return;
            }
            Invalidate(ResetReason.Navigation);
            return;
        }

        if (k.Kind == KeystrokeKind.Backspace && !hasPartialWord)
        {
            Invalidate(ResetReason.Navigation);
            return;
        }

        // A non-word char landing on an EMPTY live buffer (a quotation
        // apostrophe, a double space, an opening bracket) commits no word in the
        // tracker, yet it IS on screen. Preserve that bounded separator run in
        // the sentence model; if it cannot be represented exactly, invalidate
        // rather than rebuild a tail one char short (the eaten-letter class).
        if (k.Kind != KeystrokeKind.Text) return;
        bool empty = !hasPartialWord;
        foreach (char c in k.Text)
        {
            if (WordBoundaries.IsWordChar(c))
            {
                empty = false;
                continue;
            }
            if (empty)
            {
                if (!TryAppendTrailingSeparator(c))
                {
                    Invalidate(ResetReason.Navigation);
                    return;
                }
                if (_sentenceClosed)
                {
                    MarkCurrentSentenceReady();
                    TrySubmitNext();
                }
            }
            empty = true; // the boundary commits (or joins) the word the tracker holds
        }
    }

    private bool TryAppendTrailingSeparator(char c)
    {
        if (_buffer.Count == 0)
            return false;

        SlotEntry tail = _buffer[^1];
        if (tail.Separator.Length >= SeparatorRunCap)
            return false;

        tail.Separator += c;
        if (SentenceEnders.Contains(c))
            _sentenceClosed = true;
        return true;
    }

    private void MarkCurrentSentenceReady()
    {
        int end = _buffer.Count - 1;
        if (end < _sentenceStartIndex)
            return;
        for (int index = _sentenceStartIndex; index <= end; index++)
        {
            SlotEntry slot = _buffer[index];
            if (!slot.IsAmbiguous || slot.Resolved || slot.Ready)
                continue;
            slot.Ready = true;
            slot.ContextStart = _sentenceStartIndex;
            slot.ContextEnd = end;
        }
    }

    // Replaces the append-only model after a discontinuity with a sentence read
    // twice from the active caret. This grants no learning provenance: it only
    // reconstructs closed candidate slots for the ordinary sentence judge.
    public bool RecoverVerifiedSentence(VerifiedCaretSentence verified)
    {
        if (_disposed || !TryTokenizeVerifiedSentence(verified.Text, out List<SlotEntry> slots))
            return false;

        Invalidate(ResetReason.Navigation);
        _buffer.AddRange(slots);
        _sentenceStartIndex = 0;
        _sentenceClosed = true;
        _verifiedSentence = verified;
        MarkCurrentSentenceReady();
        TrySubmitNext();
        return true;
    }

    // Drop the sentence model: the caret left the span we model (or a backspace is
    // editing it). A verdict still in flight will be recognised as stale by epoch.
    public void Invalidate(ResetReason reason)
    {
        if (_disposed) return;

        int pendingSlots = 0;
        foreach (SlotEntry slot in _buffer)
            if (slot.IsAmbiguous && !slot.Resolved)
                pendingSlots++;
        bool currentRequestInFlight = _inFlight && _inFlightEpoch == _epoch;
        if (pendingSlots > 0)
            DeckleAutocorrectSource.Log.SentenceStageAbandoned(
                reason.ToString(), pendingSlots, currentRequestInFlight);

        _epoch++;
        _buffer.Clear();
        _sentenceClosed = false;
        _sentenceStartIndex = 0;
        _verifiedSentence = null;
        _wholeSentenceAttempted = false;
    }

    // ── Submit / apply (input thread) ────────────────────────────────────────

    private void TrySubmitNext()
    {
        if (_inFlight || _disposed) return;

        if (_wholeSentenceEnabled && !_wholeSentenceAttempted)
        {
            _wholeSentenceAttempted = true;
            RerankRequest? wholeSentence = BuildWholeSentenceRequest(out bool overflow);
            if (overflow)
            {
                ResolveReadySlots();
                DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Abstain);
                return;
            }
            if (wholeSentence is RerankRequest request)
            {
                _inFlight = true;
                _inFlightWholeSentence = true;
                _inFlightSlot = -1;
                _inFlightEpoch = _epoch;
                DeckleAutocorrectSource.Log.RerankSubmitted(-1, request.Sentence.Count);
                _lane.Submit(request);
                return;
            }

            // Global mode is one transaction or no transaction. An empty closed
            // set cannot silently reopen the former per-slot cascade.
            ResolveReadySlots();
            return;
        }

        if (_wholeSentenceEnabled)
            return;

        // Explicit compatibility mode for callers constructed without a global
        // probe. Production supplies one and can never enter this cascade.
        for (int i = 0; i < _buffer.Count; i++)
        {
            SlotEntry s = _buffer[i];
            if (s.IsAmbiguous && !s.Resolved && s.Ready && s.Candidates.Count >= 2)
            {
                _inFlight = true;
                _inFlightWholeSentence = false;
                _inFlightSlot = i;
                _inFlightEpoch = _epoch;
                RerankRequest request = BuildRequest(i);
                DeckleAutocorrectSource.Log.RerankSubmitted(i, request.Sentence.Count);
                _lane.Submit(request);
                return;
            }
        }
    }

    private RerankRequest? BuildWholeSentenceRequest(out bool overflow)
    {
        overflow = false;
        int start = -1;
        int end = -1;
        foreach (SlotEntry slot in _buffer)
        {
            if (!slot.IsAmbiguous || slot.Resolved || !slot.Ready)
                continue;
            start = start < 0 ? slot.ContextStart : Math.Min(start, slot.ContextStart);
            end = Math.Max(end, slot.ContextEnd);
        }
        if (start < 0 || end < start || end >= _buffer.Count)
            return null;

        var edits = new List<SentenceEditCandidate>();
        for (int index = start; index <= end; index++)
        {
            SlotEntry slot = _buffer[index];
            if (!slot.IsAmbiguous || slot.Resolved || !slot.Ready)
                continue;

            foreach (AccentVariant candidate in slot.WholeSentenceCandidates)
            {
                string target = CasePattern.Apply(slot.Form, candidate.Form);
                if (string.Equals(target, slot.Form, StringComparison.Ordinal))
                    continue;
                if (edits.Count == MaxSentenceEditCandidates)
                {
                    overflow = true;
                    return null;
                }
                edits.Add(new SentenceEditCandidate(index - start, target));
            }
        }
        if (edits.Count == 0)
            return null;

        var sentence = new List<string>(end - start + 1);
        for (int index = start; index <= end; index++)
            sentence.Add(_buffer[index].Form);

        _inFlightContextStart = start;
        _inFlightContextEnd = end;
        return new RerankRequest(
            Sentence: sentence,
            SlotIndex: -1,
            Candidates: Array.Empty<AccentVariant>(),
            Epoch: _epoch,
            VerifiedSentence: _verifiedSentence,
            SentenceCandidates: edits);
    }

    private void ResolveReadySlots()
    {
        foreach (SlotEntry slot in _buffer)
            if (slot.IsAmbiguous && slot.Ready)
                slot.Resolved = true;
    }

    private RerankRequest BuildRequest(int slotIndex)
    {
        SlotEntry slot = _buffer[slotIndex];
        int lo = Math.Max(slot.ContextStart, slotIndex - ContextWindow);
        int hi = Math.Min(slot.ContextEnd + 1, slotIndex + ContextWindow + 1);
        var sentence = new List<string>(hi - lo);
        for (int i = lo; i < hi; i++)
            sentence.Add(_buffer[i].Form);
        return new RerankRequest(
            sentence,
            slotIndex - lo,
            _buffer[slotIndex].Candidates,
            _epoch,
            _verifiedSentence);
    }

    // The lane delivers verdicts here, on the input thread.
    private void ApplyResult(RerankResult result)
    {
        if (_disposed) return;
        _inFlight = false;
        _inFlightEpoch = -1;
        bool wholeSentence = _inFlightWholeSentence;
        _inFlightWholeSentence = false;
        int slotIndex = _inFlightSlot;   // the absolute index we submitted
        _inFlightSlot = -1;

        RerankOutcome outcome = result.Outcome;

        if (wholeSentence)
        {
            ApplyWholeSentenceResult(result.Epoch, outcome);
            return;
        }

        // Stale: the sentence was reset (epoch bumped) under the in-flight request,
        // so the buffer no longer holds the slot we submitted. result.Epoch is the
        // freshness check; the slot is identified by our own _inFlightSlot, never by
        // result.SlotIndex (which is window-relative, for the model only).
        if (result.Epoch != _epoch
            || slotIndex < 0 || slotIndex >= _buffer.Count)
        {
            DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Stale);
            TrySubmitNext();
            return;
        }

        SlotEntry slot = _buffer[slotIndex];
        if (!slot.IsAmbiguous || slot.Resolved)
        {
            DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Resolved);
            TrySubmitNext();
            return;
        }

        // The lane is an internal component, but text injection is still a trust
        // boundary. A chosen form must belong to the exact closed set submitted
        // for this slot and must carry a finite margin that actually clears its
        // non-negative threshold. A malformed provider result becomes abstention.
        if (outcome.Chosen is not null
            && (!slot.Candidates.Any(candidate =>
                    string.Equals(candidate.Form, outcome.Chosen, StringComparison.Ordinal))
                || !double.IsFinite(outcome.Margin)
                || !double.IsFinite(outcome.Threshold)
                || outcome.Threshold < 0.0
                || outcome.Margin < outcome.Threshold))
        {
            outcome = outcome with
            {
                Chosen = null,
                AbstainReason = RerankOutcome.AbstainReasons.Error,
            };
        }

        slot.Resolved = true; // decided either way — never reconsidered

        string word = slot.Form; // the form the slot held before any rewrite
        string target = outcome.Chosen is null
            ? slot.Form
            : CasePattern.Apply(slot.Form, outcome.Chosen);

        string verdict;
        if (string.Equals(target, slot.Form, StringComparison.Ordinal))
        {
            // Nothing to write: the model declined (abstain) or affirmed the typed
            // form (equal). Either way the slot stays as the user left it.
            verdict = outcome.Chosen is null ? Outcome.Abstain : Outcome.Equal;
        }
        else
        {
            SlotRewriteResult rewrite = ApplySlotRewrite(
                slotIndex, target, CorrectionReason.SentenceReranker);
            verdict = rewrite switch
            {
                SlotRewriteResult.Applied => Outcome.Applied,
                SlotRewriteResult.Expired => Outcome.Expired,
                _ => Outcome.Blocked,
            };
        }

        DeckleAutocorrectSource.Log.RerankVerdict(verdict);
        EmitRerankDecision(slot.WordId, word, verdict, outcome);

        TrySubmitNext();
    }

    private void ApplyWholeSentenceResult(int resultEpoch, RerankOutcome outcome)
    {
        int contextStart = _inFlightContextStart;
        int contextEnd = _inFlightContextEnd;
        _inFlightContextStart = -1;
        _inFlightContextEnd = -1;

        if (resultEpoch != _epoch
            || contextStart < 0
            || contextEnd < contextStart
            || contextEnd >= _buffer.Count)
        {
            DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Stale);
            TrySubmitNext();
            return;
        }

        for (int index = contextStart; index <= contextEnd; index++)
            if (_buffer[index].IsAmbiguous && _buffer[index].Ready)
                _buffer[index].Resolved = true;

        int? relativeSlot = outcome.ChosenSlotIndex;
        if (outcome.Chosen is null || relativeSlot is null)
        {
            string verdict = outcome.AbstainReason is null
                ? Outcome.Equal
                : Outcome.Abstain;
            DeckleAutocorrectSource.Log.RerankVerdict(verdict);
            return;
        }

        int chosenSlot = contextStart + relativeSlot.Value;
        if (chosenSlot < contextStart || chosenSlot > contextEnd)
        {
            DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Abstain);
            return;
        }

        SlotEntry slot = _buffer[chosenSlot];
        bool valid = slot.WholeSentenceCandidates.Any(candidate =>
                string.Equals(
                    CasePattern.Apply(slot.Form, candidate.Form),
                    outcome.Chosen,
                    StringComparison.Ordinal))
            && double.IsFinite(outcome.Margin)
            && double.IsFinite(outcome.Threshold)
            && outcome.Threshold >= 0.0
            && outcome.Margin >= outcome.Threshold;
        if (!valid)
        {
            DeckleAutocorrectSource.Log.RerankVerdict(Outcome.Abstain);
            return;
        }

        string word = slot.Form;
        string target = outcome.Chosen;
        SlotRewriteResult rewrite = ApplySlotRewrite(
            chosenSlot, target, CorrectionReason.SentenceReranker);
        string appliedVerdict = rewrite switch
        {
            SlotRewriteResult.Applied => Outcome.Applied,
            SlotRewriteResult.Expired => Outcome.Expired,
            SlotRewriteResult.NoOp => Outcome.Equal,
            _ => Outcome.Blocked,
        };
        DeckleAutocorrectSource.Log.RerankVerdict(appliedVerdict);
        EmitRerankDecision(slot.WordId, word, appliedVerdict, outcome);
    }

    // The rerank line of the decision telemetry: the reranker's verdict for this
    // slot with its per-candidate scores and margin, joined to the synchronous
    // decision by the word id. Gated by the opt-in toggle — no rendering when off.
    private void EmitRerankDecision(long id, string word, string verdict, RerankOutcome outcome)
    {
        if (_decisionTelemetry?.Invoke() != true) return;
        DeckleAutocorrectSource.Log.AutocorrectRerankRecorded(
            id, word, verdict, outcome.Chosen ?? "", RenderScores(outcome.Scores), RenderMargin(outcome));
    }

    // "form@score|…" highest-favoured first as the reranker ranked them.
    private static string RenderScores(IReadOnlyList<RerankCandidateScore> scores)
    {
        var sb = new StringBuilder();
        foreach (RerankCandidateScore s in scores)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(s.Form).Append('@').Append(CorrectionTrace.Num(s.Score));
        }
        return sb.ToString();
    }

    // "Δ|margin_min=threshold[|why=reason]" — the top-vs-second gap against the bar.
    private static string RenderMargin(RerankOutcome outcome)
    {
        var sb = new StringBuilder();
        sb.Append(CorrectionTrace.Num(outcome.Margin))
          .Append("|margin_min=").Append(CorrectionTrace.Num(outcome.Threshold));
        if (outcome.AbstainReason is not null)
            sb.Append("|why=").Append(outcome.AbstainReason);
        return sb.ToString();
    }

    // Rewrites one slot in place by the minimal keystroke diff. Reconstructs the
    // on-screen tail from the buffer's own forms and appends the live partial word
    // identically on both sides, so InjectionPlan preserves it and the backspace
    // count is right even mid-word. Bounded by the tail length (a few words).
    // Reports whether the rewrite reached the surface, expired its bounded tail,
    // was a no-op, or met a refused (UIPI-blocked) send.
    private SlotRewriteResult ApplySlotRewrite(
        int slotIndex,
        string newForm,
        CorrectionReason reason)
    {
        SlotEntry slot = _buffer[slotIndex];
        string oldForm = slot.Form;

        var currentTail = new StringBuilder();
        var targetTail = new StringBuilder();
        for (int i = slotIndex; i < _buffer.Count; i++)
        {
            // The separator as the screen shows it: an elision commit carries its
            // apostrophe inside the form, so rendering the boundary again would
            // overstate the screen by one char per elision — one extra backspace
            // (the eaten letter) and a doubled apostrophe on reinjection.
            string separator = _buffer[i].Separator;
            currentTail.Append(_buffer[i].Form).Append(separator);
            targetTail.Append(i == slotIndex ? newForm : _buffer[i].Form).Append(separator);
        }

        string partial = _currentPartial() ?? string.Empty;
        string current = currentTail.Append(partial).ToString();
        string target = targetTail.Append(partial).ToString();
        if (string.Equals(current, target, StringComparison.Ordinal))
            return SlotRewriteResult.NoOp;

        if (current.Length > MaxRewriteTailChars || target.Length > MaxRewriteTailChars)
            return SlotRewriteResult.Expired;

        InjectionPlan plan = InjectionPlan.Compute(current, target);
        if (plan.IsNoOp)
            return SlotRewriteResult.NoOp;

        if (_injector.Replace(current, target))
        {
            slot.Form = newForm;
            if (_verifiedSentence is VerifiedCaretSentence verified)
                _verifiedSentence = verified with { Text = RenderBuffer() };
            // Realign the tracker only when the rewritten slot is the last committed
            // word (its edit window may still be open); for a word further back the
            // tracker models a later, unchanged word and is already consistent.
            if (slotIndex == _buffer.Count - 1)
                _realignLastCommitted?.Invoke(newForm);
            _onApplied?.Invoke(new CorrectionDecision(oldForm, newForm, reason), plan);
            return SlotRewriteResult.Applied;
        }

        // A partial / UIPI-blocked send leaves the tail in an unknown state.
        // Trust nothing further; drop the model rather than rewrite a half-edit.
        Invalidate(ResetReason.Navigation);
        return SlotRewriteResult.Blocked;
    }

    private bool TryTokenizeVerifiedSentence(string sentence, out List<SlotEntry> slots)
    {
        slots = new List<SlotEntry>();
        if (string.IsNullOrWhiteSpace(sentence)
            || !CaretSentenceContext.IsTerminalPunctuation(sentence[^1]))
            return false;

        var word = new StringBuilder();
        foreach (char current in sentence)
        {
            if (WordBoundaries.IsWordChar(current))
            {
                word.Append(current);
                continue;
            }

            if (WordBoundaries.IsApostrophe(current) && word.Length > 0)
            {
                word.Append(current);
                if (WordBoundaries.IsElisionPrefix(word.ToString(0, word.Length - 1)))
                    CommitRecoveredWord(word, slots);
                continue;
            }

            if (word.Length > 0)
                CommitRecoveredWord(word, slots);

            if (slots.Count == 0 || slots[^1].Separator.Length >= SeparatorRunCap)
                return false;
            slots[^1].Separator += current;
        }

        if (word.Length > 0)
            CommitRecoveredWord(word, slots);

        if (slots.Count is 0 or > BufferCap)
            return false;

        foreach (SlotEntry slot in slots)
        {
            // Curly apostrophes are screen-exact but the candidate lexicons are
            // normalized to ASCII. Leave such tokens untouched in the first
            // recovery version instead of silently changing typography.
            if (slot.Form.Contains('’')) continue;
            IReadOnlyList<AccentVariant> candidates =
                _probe.SentenceCandidates(slot.Form, includeTypedLiteral: true);
            IReadOnlyList<AccentVariant> wholeSentenceCandidates =
                _wholeSentenceProbe.SentenceCandidates(
                    slot.Form, includeTypedLiteral: true);
            if (candidates.Count < 2 && wholeSentenceCandidates.Count < 2) continue;
            slot.IsAmbiguous = true;
            slot.Candidates = candidates;
            slot.WholeSentenceCandidates = wholeSentenceCandidates;
            DeckleAutocorrectSource.Log.RerankSlotPending(
                Math.Max(candidates.Count, wholeSentenceCandidates.Count),
                slot.Form.Length);
        }

        return true;
    }

    private static void CommitRecoveredWord(StringBuilder word, List<SlotEntry> slots)
    {
        slots.Add(new SlotEntry { Form = word.ToString() });
        word.Clear();
    }

    private string RenderBuffer()
    {
        var text = new StringBuilder();
        foreach (SlotEntry slot in _buffer)
            text.Append(slot.Form).Append(slot.Separator);
        return text.ToString();
    }

    public void Dispose()
    {
        _disposed = true;
        _lane.ResultSink = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // The closed vocabulary for the rerank verdict log — one spelling, one place,
    // so a grep on an outcome finds every occurrence (logging doctrine).
    private static class Outcome
    {
        public const string Applied  = "applied";
        public const string Equal    = "equal";
        public const string Abstain  = "abstain";
        public const string Stale    = "stale";
        public const string Resolved = "resolved";
        public const string Blocked  = "blocked";
        public const string Expired  = "expired";
    }

    // One word in the rolling sentence model. Mutable: Form is rewritten in place
    // when a slot resolves so later tail reconstructions see the corrected text.
    private sealed class SlotEntry
    {
        public string Form = string.Empty;
        public string Separator = string.Empty;
        public IReadOnlyList<AccentVariant> Candidates = Array.Empty<AccentVariant>();
        public IReadOnlyList<AccentVariant> WholeSentenceCandidates =
            Array.Empty<AccentVariant>();
        public bool IsAmbiguous;
        public bool Resolved;
        public bool Ready;
        public int ContextStart;
        public int ContextEnd;
        // The engine's per-word id, carried so the deferred reranker verdict joins
        // the synchronous decision record on the same id in the decision telemetry.
        public long WordId;
    }

    private enum SlotRewriteResult
    {
        Applied,
        NoOp,
        Expired,
        Blocked,
    }
}
