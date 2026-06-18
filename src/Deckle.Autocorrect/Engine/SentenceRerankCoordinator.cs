using System.Text;

namespace Deckle.Autocorrect;

// One unit of work handed to the background reranker: the sentence as ordered
// word-forms (a bounded window around the slot), the slot to resolve within that
// window, its closed candidate set, and the buffer epoch the request was built
// under — so a result arriving after a reset is recognised as stale and dropped.
public readonly record struct RerankRequest(
    IReadOnlyList<string> Sentence, int SlotIndex, IReadOnlyList<AccentVariant> Candidates, int Epoch);

// The reranker's verdict for one slot: the full outcome (chosen form plus the
// per-candidate scores and margin for the decision telemetry), tagged with the
// buffer index and epoch it was computed against.
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
// On each committed word it records the post-gate surface form, asks the probe
// whether the slot is a real-word ambiguity the gate left alone (la/là, a/à,
// ou/où), and — once enough right-context has arrived (or a sentence-ender flushes
// it) — submits ONE rerank at a time. When the verdict returns it rewrites the
// slot in place: it reconstructs the on-screen tail from its own buffer (never
// reads the screen) and appends the live partial word verbatim, so the minimal-diff
// injector backspaces exactly the right amount even while the user keeps typing.
//
// Staleness is the danger and the guards are: an epoch bumped on every reset
// (focus, pointer, Enter, a Backspace into committed text) drops any verdict that
// outlived its sentence; the tail is always rebuilt from the live buffer, so words
// committed during the ~100 ms inference are absorbed, not corrupted; a failed
// (UIPI-blocked) send invalidates the whole model rather than rewrite against a
// half-edited tail. A reranked slot sits behind the caret, so it does NOT arm the
// one-Backspace revert.
//
// Sentence-initial capitalization rides the same buffer and reinjection path,
// deterministically (no ONNX): the first word of a vouched sentence — one that
// began after a real sentence-ender or Enter, never after a mere caret move — is
// raised to a capital, composed on top of any diacritic verdict for that slot.
public sealed class SentenceRerankCoordinator : IDisposable
{
    // Right-context words to wait for before resolving a slot (Louis's "2-3
    // words"); a sentence-ender flushes earlier. Calibration constant, not exposed.
    private const int DeferralWords = 3;

    // The buffer is a rolling window; past this many words it drops to a fresh
    // sentence rather than grow unbounded.
    private const int BufferCap = 40;

    // Words on each side of the slot handed to the masked-LM. Local context is
    // what resolves la/là; a wide window only slows inference and risks >512 tokens.
    private const int ContextWindow = 12;

    // A boundary that ends a clause enough to decide its pending slots now.
    private static readonly HashSet<char> SentenceBreaks = new() { '.', '!', '?', ';', ':', '…' };

    // A boundary after which the next word starts a new sentence (gets a capital).
    // Tighter than SentenceBreaks: ';' and ':' continue the sentence lowercase.
    private static readonly HashSet<char> CapitalizingEnders = new() { '.', '!', '?', '…' };

    private readonly IRerankLane _lane;
    private readonly IAmbiguityProbe _probe;
    private readonly ITextInjector _injector;
    private readonly Func<string> _currentPartial;
    private readonly Action<string>? _realignLastCommitted;
    private readonly Action<CorrectionDecision>? _onApplied;
    // Gate for the per-word decision telemetry: when it returns true the deferred
    // reranker verdict is emitted to the autocorrect.decisions dataset (scores and
    // margin included). Null or false = no rerank line, no rendering cost.
    private readonly Func<bool>? _decisionTelemetry;

    private readonly List<SlotEntry> _buffer = new();
    private int _epoch;
    private bool _inFlight;
    // The ABSOLUTE buffer index of the slot currently submitted. The request
    // carries a window-relative index (for the model), so the verdict cannot be
    // trusted to identify the slot — single-flight lets us remember it here.
    private int _inFlightSlot = -1;
    private bool _nextWordIsSentenceInitial;
    private int _pendingCapSlot = -1;
    private bool _disposed;

    public SentenceRerankCoordinator(
        IRerankLane lane,
        IAmbiguityProbe probe,
        ITextInjector injector,
        Func<string> currentPartial,
        Action<string>? realignLastCommitted = null,
        Action<CorrectionDecision>? onApplied = null,
        Func<bool>? decisionTelemetry = null)
    {
        _lane = lane;
        _probe = probe;
        _injector = injector;
        _currentPartial = currentPartial;
        _realignLastCommitted = realignLastCommitted;
        _onApplied = onApplied;
        _decisionTelemetry = decisionTelemetry;
        _lane.ResultSink = ApplyResult;
    }

    // ── Input-thread feed ────────────────────────────────────────────────────

    // A word committed (after the synchronous gate ran). finalForm is what is on
    // screen; gateLeftLiteral is true when the gate corrected nothing — the only
    // case a real-word ambiguity survives for the reranker. wordId is the engine's
    // per-word id, carried so a deferred verdict joins its synchronous decision line.
    public void OnWordCommitted(string finalForm, char boundary, bool gateLeftLiteral, long wordId = 0)
    {
        if (_disposed) return;

        // The vouch flag is the authority — set after Enter or a sentence-ender and
        // consumed here. It is NOT gated on an empty buffer: the rolling buffer keeps
        // prior sentences, so the first word after a period commits with the flag set
        // while earlier words are still present.
        bool sentenceInitial = _nextWordIsSentenceInitial;
        _nextWordIsSentenceInitial = false;

        var entry = new SlotEntry
        {
            Form = finalForm,
            Boundary = boundary,
            IsSentenceInitial = sentenceInitial,
            WordId = wordId,
        };

        if (gateLeftLiteral)
        {
            var candidates = _probe.AmbiguousCandidates(finalForm);
            if (candidates.Count >= 2)
            {
                entry.IsAmbiguous = true;
                entry.Candidates = candidates;
                DeckleAutocorrectSource.Log.RerankSlotPending(candidates.Count, finalForm.Length);
            }
        }

        if (sentenceInitial && NeedsCapitalization(finalForm))
            entry.NeedsCap = true;

        // Right-context grows for every still-open ambiguous slot before this one.
        foreach (SlotEntry s in _buffer)
            if (s.IsAmbiguous && !s.Resolved)
                s.RightContextCount++;

        _buffer.Add(entry);

        // A capitalization scheduled on a PRIOR commit applies now — one word
        // behind, so it shares the transparent "a beat late" feel and never
        // fights the same-commit revert arming of the gate.
        if (_pendingCapSlot >= 0 && _pendingCapSlot < _buffer.Count)
        {
            ApplyCapitalizationOnly(_pendingCapSlot);
            _pendingCapSlot = -1;
        }

        // A non-ambiguous first word's capital is scheduled for the next commit.
        // An ambiguous first word composes its capital onto the diacritic verdict.
        if (entry.NeedsCap && !entry.IsAmbiguous)
            _pendingCapSlot = _buffer.Count - 1;

        // A sentence-ender decides every still-open slot "from the sentence so far".
        if (SentenceBreaks.Contains(boundary))
            foreach (SlotEntry s in _buffer)
                if (s.IsAmbiguous && !s.Resolved)
                    s.RightContextCount = Math.Max(s.RightContextCount, DeferralWords);

        if (CapitalizingEnders.Contains(boundary))
            _nextWordIsSentenceInitial = true;

        if (_buffer.Count > BufferCap)
        {
            Invalidate(ResetReason.BufferLimit);
            return;
        }

        TrySubmitNext();
    }

    // Every physical keystroke, with the live word as it stood BEFORE the tracker
    // consumed it. A Backspace on an empty live buffer re-opens a committed word
    // for editing — our append-only model can no longer mirror the screen, so we
    // start fresh. (Resets proper arrive via Invalidate.)
    public void NotePhysicalKey(Keystroke k, string preBuffer)
    {
        if (_disposed) return;
        if (k.Kind == KeystrokeKind.Backspace && preBuffer.Length == 0)
            Invalidate(ResetReason.Navigation);
    }

    // Drop the sentence model: the caret left the span we model (or a backspace is
    // editing it). A verdict still in flight will be recognised as stale by epoch.
    public void Invalidate(ResetReason reason)
    {
        if (_disposed) return;
        _epoch++;
        _buffer.Clear();
        _pendingCapSlot = -1;
        // Enter begins a new sentence (capital vouched); any other reset is a caret
        // move to an unknown position, where capitalizing would be a guess.
        _nextWordIsSentenceInitial = reason == ResetReason.Enter;
    }

    // ── Submit / apply (input thread) ────────────────────────────────────────

    private void TrySubmitNext()
    {
        if (_inFlight || _disposed) return;
        for (int i = 0; i < _buffer.Count; i++)
        {
            SlotEntry s = _buffer[i];
            if (s.IsAmbiguous && !s.Resolved && s.RightContextCount >= DeferralWords)
            {
                _inFlight = true;
                _inFlightSlot = i;
                RerankRequest request = BuildRequest(i);
                DeckleAutocorrectSource.Log.RerankSubmitted(i, request.Sentence.Count);
                _lane.Submit(request);
                return;
            }
        }
    }

    private RerankRequest BuildRequest(int slotIndex)
    {
        int lo = Math.Max(0, slotIndex - ContextWindow);
        int hi = Math.Min(_buffer.Count, slotIndex + ContextWindow + 1);
        var sentence = new List<string>(hi - lo);
        for (int i = lo; i < hi; i++)
            sentence.Add(_buffer[i].Form);
        return new RerankRequest(sentence, slotIndex - lo, _buffer[slotIndex].Candidates, _epoch);
    }

    // The lane delivers verdicts here, on the input thread.
    private void ApplyResult(RerankResult result)
    {
        if (_disposed) return;
        _inFlight = false;
        int slotIndex = _inFlightSlot;   // the absolute index we submitted
        _inFlightSlot = -1;

        RerankOutcome outcome = result.Outcome;

        // Stale: the sentence was reset (epoch bumped) under the in-flight request,
        // so the buffer no longer holds the slot we submitted. result.Epoch is the
        // freshness check; the slot is identified by our own _inFlightSlot, never by
        // result.SlotIndex (which is window-relative, for the model only).
        if (result.Epoch != _epoch || slotIndex < 0 || slotIndex >= _buffer.Count)
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

        slot.Resolved = true; // decided either way — never reconsidered

        string word = slot.Form; // the form the slot held before any rewrite
        string target = outcome.Chosen ?? slot.Form;
        if (slot.NeedsCap)
            target = Capitalize(target);

        string verdict;
        if (string.Equals(target, slot.Form, StringComparison.Ordinal))
        {
            // Nothing to write: the model declined (abstain) or affirmed the typed
            // form (equal). Either way the slot stays as the user left it.
            verdict = outcome.Chosen is null ? Outcome.Abstain : Outcome.Equal;
        }
        else
        {
            CorrectionReason reason = outcome.Chosen is not null
                ? CorrectionReason.SentenceReranker
                : CorrectionReason.Capitalization;
            bool applied = ApplySlotRewrite(slotIndex, target, reason);
            verdict = applied ? Outcome.Applied : Outcome.Blocked;
        }

        DeckleAutocorrectSource.Log.RerankVerdict(verdict);
        EmitRerankDecision(slot.WordId, word, verdict, outcome);

        TrySubmitNext();
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

    private void ApplyCapitalizationOnly(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _buffer.Count)
            return;
        SlotEntry slot = _buffer[slotIndex];
        if (slot.Resolved)
            return;
        slot.Resolved = true;
        string target = Capitalize(slot.Form);
        if (!string.Equals(target, slot.Form, StringComparison.Ordinal))
            ApplySlotRewrite(slotIndex, target, CorrectionReason.Capitalization);
    }

    // Rewrites one slot in place by the minimal keystroke diff. Reconstructs the
    // on-screen tail from the buffer's own forms and appends the live partial word
    // identically on both sides, so InjectionPlan preserves it and the backspace
    // count is right even mid-word. Bounded by the tail length (a few words).
    // Returns true when the rewrite reached the surface, false on a no-op diff or
    // a refused (UIPI-blocked) send — the caller turns that into the verdict.
    private bool ApplySlotRewrite(int slotIndex, string newForm, CorrectionReason reason)
    {
        SlotEntry slot = _buffer[slotIndex];
        string oldForm = slot.Form;

        var currentTail = new StringBuilder();
        var targetTail = new StringBuilder();
        for (int i = slotIndex; i < _buffer.Count; i++)
        {
            currentTail.Append(_buffer[i].Form).Append(_buffer[i].Boundary);
            targetTail.Append(i == slotIndex ? newForm : _buffer[i].Form).Append(_buffer[i].Boundary);
        }

        string partial = _currentPartial() ?? string.Empty;
        string current = currentTail.Append(partial).ToString();
        string target = targetTail.Append(partial).ToString();
        if (string.Equals(current, target, StringComparison.Ordinal))
            return false;

        if (_injector.Replace(current, target))
        {
            slot.Form = newForm;
            // Realign the tracker only when the rewritten slot is the last committed
            // word (its edit window may still be open); for a word further back the
            // tracker models a later, unchanged word and is already consistent.
            if (slotIndex == _buffer.Count - 1)
                _realignLastCommitted?.Invoke(newForm);
            _onApplied?.Invoke(new CorrectionDecision(oldForm, newForm, reason));
            return true;
        }

        // A partial / UIPI-blocked send leaves the tail in an unknown state.
        // Trust nothing further; drop the model rather than rewrite a half-edit.
        Invalidate(ResetReason.Navigation);
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _lane.ResultSink = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // A first word qualifies for a capital when it is a lowercase-initial plain
    // word; an internal capital (iPhone, eBay) means the user meant that casing.
    private static bool NeedsCapitalization(string form) =>
        form.Length > 0
        && char.IsLetter(form[0])
        && char.IsLower(form[0])
        && !WordShape.HasInternalUpper(form);

    private static string Capitalize(string form) =>
        form.Length == 0 ? form : char.ToUpperInvariant(form[0]) + form[1..];

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
    }

    // One word in the rolling sentence model. Mutable: Form is rewritten in place
    // when a slot resolves so later tail reconstructions see the corrected text.
    private sealed class SlotEntry
    {
        public string Form = string.Empty;
        public char Boundary;
        public IReadOnlyList<AccentVariant> Candidates = Array.Empty<AccentVariant>();
        public bool IsAmbiguous;
        public bool Resolved;
        public int RightContextCount;
        public bool NeedsCap;
        public bool IsSentenceInitial;
        // The engine's per-word id, carried so the deferred reranker verdict joins
        // the synchronous decision record on the same id in the decision telemetry.
        public long WordId;
    }
}
