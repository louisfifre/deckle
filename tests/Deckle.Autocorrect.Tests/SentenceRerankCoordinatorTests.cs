using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The live second stage in isolation: the sentence buffer, the deferral trigger,
// the staleness guards, and the reinjection-string construction — driven through a
// synchronous (or manually-stepped) fake lane, a fake probe and a recording
// injector, so the risky concurrency logic is exercised deterministically with no
// real thread, no ONNX, no desktop.
[Trait("Category", "unit")]
public class SentenceRerankCoordinatorTests
{
    private static AccentVariant[] LaLà() =>
        new[] { new AccentVariant("la", 9000.0), new AccentVariant("là", 50.0) };

    private static FakeProbe ProbeForLa() =>
        new(new Dictionary<string, AccentVariant[]> { ["la"] = LaLà() });

    // ── The contextual correction ──────────────────────────────────────────

    [Fact]
    public void ResolvesAmbiguousSlotAfterThreeRightContextWords()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', gateLeftLiteral: true);
        coord.OnWordCommitted("la", ' ', gateLeftLiteral: true);     // ambiguous slot, index 1
        coord.OnWordCommitted("mer", ' ', gateLeftLiteral: true);    // right-context 1
        coord.OnWordCommitted("est", ' ', gateLeftLiteral: true);    // right-context 2
        coord.OnWordCommitted("belle", ' ', gateLeftLiteral: true);  // right-context 3 → fires

        Assert.Single(inj.Calls);
        Assert.Equal("la mer est belle ", inj.Calls[0].Current);
        Assert.Equal("là mer est belle ", inj.Calls[0].Target);
    }

    // The regression the whole gesture exists for (JOURNAL 2026-07-02). An
    // elision commit (« l' », boundary '\'') carries its apostrophe INSIDE the
    // form; rendering the boundary again would overstate the on-screen tail by
    // one char per elision — one extra backspace (an eaten letter) and a doubled
    // apostrophe on reinjection. The rewrite strings must equal the exact screen.
    [Fact]
    public void SlotRewriteAcrossAnElisionTailMatchesTheScreen()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', gateLeftLiteral: true);
        coord.OnWordCommitted("la", ' ', gateLeftLiteral: true);      // ambiguous slot
        coord.OnWordCommitted("l'", '\'', gateLeftLiteral: true);     // elision in the tail
        coord.OnWordCommitted("eau", ' ', gateLeftLiteral: true);     // right-context 2
        coord.OnWordCommitted("froide", ' ', gateLeftLiteral: true);  // right-context 3 → fires

        Assert.Single(inj.Calls);
        // « l' » collapses its separator: no space, no doubled apostrophe.
        Assert.Equal("la l'eau froide ", inj.Calls[0].Current);
        Assert.Equal("là l'eau froide ", inj.Calls[0].Target);
    }

    [Fact]
    public void AbstainLeavesTheSlotUntouched()
    {
        var lane = new TestRerankLane { Reranker = _ => null }; // model not confident
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void SentenceEnderDecidesTheSlotImmediately()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("ici", ' ', true);
        coord.OnWordCommitted("la", '.', true); // sentence-ender flushes the pending slot now

        Assert.Single(inj.Calls);
        Assert.Equal("la.", inj.Calls[0].Current);
        Assert.Equal("là.", inj.Calls[0].Target);
    }

    [Fact]
    public void CommitStageDiacriticsCorrectionCanBeTakenBackToTheTypedOriginal()
    {
        var lane = new TestRerankLane
        {
            Reranker = req =>
            {
                Assert.Contains(req.Candidates, c => c.Form == "la");
                Assert.Contains(req.Candidates, c => c.Form == "là");
                return "la"; // full context says the commit-stage accent was wrong
            }
        };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", "là", ' ', sentenceMayEvaluate: true); // typed original joins candidates
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Single(inj.Calls);
        Assert.Equal("là mer est belle ", inj.Calls[0].Current);
        Assert.Equal("la mer est belle ", inj.Calls[0].Target);
    }

    [Fact]
    public void NonRerankableCommitCorrectionIsNeverAnAmbiguousSlot()
    {
        // sentenceMayEvaluate=false means the commit-stage edit is outside the
        // sentence reranker's rights (typo, elision, grammar). It must not
        // reconsider it even if the typed form folds ambiguously.
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", "là", ' ', sentenceMayEvaluate: false);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void PreservesTheLivePartialWordOnBothSides()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        // The user is mid-typing "me" when the verdict lands.
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "me");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Single(inj.Calls);
        Assert.EndsWith("me", inj.Calls[0].Current);
        Assert.EndsWith("me", inj.Calls[0].Target);
    }

    [Fact]
    public void ResolvesASlotBeyondTheContextWindow()
    {
        // A slot more than ContextWindow (12) words into the sentence: the request
        // carries a window-relative index, but the verdict must rewrite the correct
        // ABSOLUTE slot — not a word twelve positions earlier. This is the
        // relative/absolute mismatch that produced a resubmit storm of "resolved".
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        for (int k = 0; k < 13; k++)
            coord.OnWordCommitted("mot", ' ', gateLeftLiteral: true); // unambiguous
        coord.OnWordCommitted("la", ' ', gateLeftLiteral: true);      // slot index 13
        coord.OnWordCommitted("x", ' ', true);
        coord.OnWordCommitted("y", ' ', true);
        coord.OnWordCommitted("z", ' ', true);                        // 3 words → fires

        Assert.Single(inj.Calls);
        Assert.StartsWith("la ", inj.Calls[0].Current);
        Assert.StartsWith("là ", inj.Calls[0].Target);
    }

    // ── Staleness guards ────────────────────────────────────────────────────

    [Fact]
    public void ResetUnderAnInFlightVerdictDropsIt()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);
        Assert.Single(lane.Submitted); // request captured, not yet delivered

        coord.Invalidate(ResetReason.FocusChanged); // the sentence is gone under it
        lane.DeliverLast();                         // stale epoch → dropped

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void BackspaceIntoCommittedTextInvalidatesPendingVerdict()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        // Backspace with an empty live buffer = re-opening a committed word.
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Backspace, "", 0), hasPartialWord: false);
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void BackspaceWithinThePartialWordDoesNotInvalidate()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "me");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        // Backspace while typing a word (non-empty live buffer) just shortens it.
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Backspace, "", 0), hasPartialWord: true);
        lane.DeliverLast();

        Assert.Single(inj.Calls); // still applied
    }

    // A non-word char on an EMPTY live buffer (a quotation apostrophe, a double
    // space, a bracket) is on screen but commits nothing — the tracker swallows
    // it as noise. The model can no longer mirror the screen and must drop,
    // rather than let a later slot rewrite rebuild a tail one char short of the
    // screen and eat a letter on reinjection.
    [Fact]
    public void NoiseCharOnAnEmptyBufferDropsThePendingSlot()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', gateLeftLiteral: true); // ambiguous slot
        // A quotation apostrophe typed on an empty live buffer: « la 'belle… ».
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, "'", 0), hasPartialWord: false);
        coord.OnWordCommitted("belle", ' ', true);
        coord.OnWordCommitted("histoire", ' ', true);
        coord.OnWordCommitted("non", ' ', true);

        Assert.Empty(lane.Submitted); // the slot died with the model
        Assert.Empty(inj.Calls);      // nothing rewrites against a screen we lost
    }

    [Fact]
    public void NoiseCharUnderAnInFlightVerdictDropsIt()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);
        Assert.Single(lane.Submitted);

        // An extra space typed during the inference: on screen, unmodeled.
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: false);
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void ABoundaryEndingAWordDoesNotDropTheModel()
    {
        // Each committing boundary reaches NotePhysicalKey with the word still in
        // the live buffer — the normal flow must never be mistaken for noise.
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: true);
        coord.OnWordCommitted("la", ' ', true);
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: true);
        coord.OnWordCommitted("est", ' ', true);
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Single(inj.Calls);
    }

    // ── Capitalization ──────────────────────────────────────────────────────

    [Fact]
    public void CapitalizesTheFirstWordOfAVouchedSentence()
    {
        var lane = new TestRerankLane();
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, new FakeProbe(new()), inj, () => "");

        coord.Invalidate(ResetReason.Enter);             // a new line vouches a sentence start
        coord.OnWordCommitted("bonjour", ' ', true);     // first word, lowercase initial
        coord.OnWordCommitted("tout", ' ', true);        // a beat later → capital applied

        Assert.Single(inj.Calls);
        Assert.Equal("bonjour tout ", inj.Calls[0].Current);
        Assert.Equal("Bonjour tout ", inj.Calls[0].Target);
    }

    [Fact]
    public void DoesNotCapitalizeWithoutAVouchedSentenceStart()
    {
        var lane = new TestRerankLane();
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, new FakeProbe(new()), inj, () => "");

        // No Enter / sentence-ender to vouch a start (e.g. a mid-paragraph click).
        coord.OnWordCommitted("bonjour", ' ', true);
        coord.OnWordCommitted("tout", ' ', true);

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void CapitalizesAfterASentenceEndingPeriod()
    {
        var lane = new TestRerankLane();
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, new FakeProbe(new()), inj, () => "");

        coord.OnWordCommitted("fin", '.', true);      // '.' vouches the next word
        coord.OnWordCommitted("bonjour", ' ', true);  // first word of the new sentence
        coord.OnWordCommitted("tout", ' ', true);     // a beat later → capital applied

        Assert.Single(inj.Calls);
        Assert.Equal("bonjour tout ", inj.Calls[0].Current);
        Assert.Equal("Bonjour tout ", inj.Calls[0].Target);
    }

    [Fact]
    public void SentenceInitialVouchSurvivesANoiseDrop()
    {
        // Punctuation noise does not move the sentence boundary: a second space
        // after the period drops the model but the next word keeps its capital.
        var lane = new TestRerankLane();
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, new FakeProbe(new()), inj, () => "");

        coord.OnWordCommitted("fin", '.', true); // '.' vouches the next word
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: false);
        coord.OnWordCommitted("bonjour", ' ', true);
        coord.OnWordCommitted("tout", ' ', true); // a beat later → capital applied

        Assert.Single(inj.Calls);
        Assert.Equal("bonjour tout ", inj.Calls[0].Current);
        Assert.Equal("Bonjour tout ", inj.Calls[0].Target);
    }

    [Fact]
    public void LeavesAnInternalCapitalWordAlone()
    {
        var lane = new TestRerankLane();
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, new FakeProbe(new()), inj, () => "");

        coord.Invalidate(ResetReason.Enter);
        coord.OnWordCommitted("iPhone", ' ', true); // user meant the casing
        coord.OnWordCommitted("est", ' ', true);

        Assert.Empty(inj.Calls);
    }

    // ── The pause pass (CONTEXT.md § Pause pass) ────────────────────────────

    [Fact]
    public void FlushOnPauseDecidesTheOpenSlotBeforeItsDeferralBar()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);   // ambiguous, 0 right context
        coord.OnWordCommitted("suite", ' ', true); // right-context 1 — under the bar

        Assert.Empty(inj.Calls);               // nothing fires on its own
        Assert.Equal(1, coord.FlushOnPause()); // the one open slot is put in motion

        Assert.Single(inj.Calls);
        Assert.Equal("la suite ", inj.Calls[0].Current);
        Assert.Equal("là suite ", inj.Calls[0].Target);
    }

    [Fact]
    public void FlushOnPauseWithNothingOpenIsSilent()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), new RecordingInjector(), () => "");

        coord.OnWordCommitted("bonjour", ' ', true); // no ambiguity anywhere

        Assert.Equal(0, coord.FlushOnPause());
        Assert.Empty(lane.Submitted);
    }

    [Fact]
    public void TheTrueClosureReviewsAPauseVerdictAndCanTakeItBack()
    {
        // The pause pass chose « là » early; the full sentence says « la ». The
        // closure re-opens the pause-flushed slot — the typed original is still
        // among the candidates — and the premature verdict is silently undone.
        int calls = 0;
        var lane = new TestRerankLane { Reranker = _ => ++calls == 1 ? "là" : "la" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.FlushOnPause();                       // premature verdict: « là »
        coord.OnWordCommitted("vie", '.', true);    // true closure → re-review: « la »

        Assert.Equal(2, lane.Submitted.Count);
        Assert.Equal(2, inj.Calls.Count);
        Assert.Equal("là ", inj.Calls[0].Target);   // the pause wrote it…
        Assert.Equal("la vie.", inj.Calls[1].Target); // …the closure took it back
    }

    [Fact]
    public void ANaturallyResolvedSlotIsNotReReviewedAtClosure()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);  // deferral met — resolves naturally
        coord.OnWordCommitted("ici", '.', true);    // closure: nothing to re-open

        Assert.Single(lane.Submitted);
    }

    // ── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeProbe : IAmbiguityProbe
    {
        private readonly Dictionary<string, AccentVariant[]> _map;
        public FakeProbe(Dictionary<string, AccentVariant[]> map) => _map = map;

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) => Candidates(word, false);

        public IReadOnlyList<AccentVariant> SentenceCandidates(string word, bool includeTypedLiteral) =>
            Candidates(word, includeTypedLiteral);

        private IReadOnlyList<AccentVariant> Candidates(string word, bool includeTypedLiteral)
        {
            string lower = word.ToLowerInvariant();
            var v = _map.TryGetValue(lower, out var mapped)
                ? new List<AccentVariant>(mapped)
                : new List<AccentVariant>();
            if (includeTypedLiteral && v.TrueForAll(c => c.Form != lower))
                v.Add(new AccentVariant(lower, 0.0));
            return v.Count >= 2 ? v : System.Array.Empty<AccentVariant>();
        }
    }

    // A lane that runs the (fake) reranker synchronously, or — in Manual mode —
    // only captures the request so a test can reset state before delivering it.
    private sealed class TestRerankLane : IRerankLane
    {
        public Action<RerankResult>? ResultSink { get; set; }
        public readonly List<RerankRequest> Submitted = new();
        public Func<RerankRequest, string?>? Reranker;
        public bool Manual;

        public void Submit(RerankRequest request)
        {
            Submitted.Add(request);
            if (!Manual) Deliver(request);
        }

        public void DeliverLast() => Deliver(Submitted[^1]);

        private void Deliver(RerankRequest request)
        {
            string? chosen = Reranker?.Invoke(request);
            RerankOutcome outcome = chosen is null
                ? RerankOutcome.Abstained(RerankOutcome.AbstainReasons.BelowMargin)
                : new RerankOutcome(chosen, System.Array.Empty<RerankCandidateScore>(), 0.0, 0.0, null);
            ResultSink?.Invoke(new RerankResult(request.SlotIndex, request.Epoch, outcome));
        }

        public void Dispose() { }
    }
}
