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
    public void GateCorrectedWordIsNeverAnAmbiguousSlot()
    {
        // gateLeftLiteral=false means the synchronous gate already acted: the
        // reranker must not reconsider it, even if its form folds ambiguously.
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("c'est", ' ', true);
        coord.OnWordCommitted("la", ' ', gateLeftLiteral: false); // not a literal the gate left
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
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Backspace, "", 0), preBuffer: "");
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
        coord.NotePhysicalKey(new Keystroke(KeystrokeKind.Backspace, "", 0), preBuffer: "me");
        lane.DeliverLast();

        Assert.Single(inj.Calls); // still applied
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

    // ── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeProbe : IAmbiguityProbe
    {
        private readonly Dictionary<string, AccentVariant[]> _map;
        public FakeProbe(Dictionary<string, AccentVariant[]> map) => _map = map;

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            _map.TryGetValue(word.ToLowerInvariant(), out var v)
                ? v
                : System.Array.Empty<AccentVariant>();
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
            ResultSink?.Invoke(new RerankResult(request.SlotIndex, request.Epoch, chosen));
        }

        public void Dispose() { }
    }
}
