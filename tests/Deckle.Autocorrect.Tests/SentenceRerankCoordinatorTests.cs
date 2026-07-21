using System.Collections.Generic;
using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The live second stage in isolation. These tests pin the safety contract: the
// model may judge only a closed sentence, and its verdict expires on the first
// physical key or any other evidence that the visible target changed.
[Trait("Category", "unit")]
public class SentenceRerankCoordinatorTests
{
    private static AccentVariant[] LaLà() =>
        new[] { new AccentVariant("la", 9000.0), new AccentVariant("là", 50.0) };

    private static FakeProbe ProbeForLa() =>
        new(new Dictionary<string, AccentVariant[]> { ["la"] = LaLà() });

    [Fact]
    public void DoesNotSubmitBeforeTerminalSentenceClosure()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var coord = new SentenceRerankCoordinator(
            lane, ProbeForLa(), new RecordingInjector(), () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", ' ', true);
        coord.OnWordCommitted("est", ' ', true);
        coord.OnWordCommitted("belle", ' ', true);

        Assert.Empty(lane.Submitted);
    }

    [Theory]
    [InlineData('.')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData('…')]
    public void TerminalSentenceEnderSubmitsPendingSlot(char ender)
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("ici", ' ', true);
        coord.OnWordCommitted("la", ender, true);

        Assert.Single(lane.Submitted);
        Assert.Single(inj.Calls);
        Assert.Equal($"la{ender}", inj.Calls[0].Current);
        Assert.Equal($"là{ender}", inj.Calls[0].Target);
    }

    [Theory]
    [InlineData(';')]
    [InlineData(':')]
    public void ClausePunctuationDoesNotCloseTheSentence(char boundary)
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var coord = new SentenceRerankCoordinator(
            lane, ProbeForLa(), new RecordingInjector(), () => "");

        coord.OnWordCommitted("la", boundary, true);

        Assert.Empty(lane.Submitted);
    }

    [Fact]
    public void SlotRewriteAcrossAnElisionTailMatchesTheClosedScreen()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("l'", '\'', true);
        coord.OnWordCommitted("eau", ' ', true);
        coord.OnWordCommitted("froide", '.', true);

        Assert.Single(inj.Calls);
        Assert.Equal("la l'eau froide.", inj.Calls[0].Current);
        Assert.Equal("là l'eau froide.", inj.Calls[0].Target);
    }

    [Fact]
    public void AbstainLeavesTheClosedSentenceUntouched()
    {
        var lane = new TestRerankLane { Reranker = _ => null };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", '.', true);

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void CommitStageDiacriticsCorrectionCanBeTakenBackAtClosure()
    {
        var lane = new TestRerankLane
        {
            Reranker = request =>
            {
                Assert.Contains(request.Candidates, candidate => candidate.Form == "la");
                Assert.Contains(request.Candidates, candidate => candidate.Form == "là");
                return "la";
            }
        };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", "là", '.', sentenceMayEvaluate: true);

        Assert.Single(inj.Calls);
        Assert.Equal("là.", inj.Calls[0].Current);
        Assert.Equal("la.", inj.Calls[0].Target);
    }

    [Fact]
    public void NonRerankableCommitCorrectionIsNeverSubmitted()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var coord = new SentenceRerankCoordinator(
            lane, ProbeForLa(), new RecordingInjector(), () => "");

        coord.OnWordCommitted("la", "là", '.', sentenceMayEvaluate: false);

        Assert.Empty(lane.Submitted);
    }

    [Fact]
    public void ResolvesASlotBeyondTheContextWindowAtClosure()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        for (int index = 0; index < 13; index++)
            coord.OnWordCommitted("mot", ' ', true);
        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("fin", '.', true);

        Assert.Single(inj.Calls);
        Assert.StartsWith("la ", inj.Calls[0].Current);
        Assert.StartsWith("là ", inj.Calls[0].Target);
    }

    [Fact]
    public void ResetUnderAnInFlightVerdictDropsIt()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", '.', true);
        coord.Invalidate(ResetReason.FocusChanged);
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void FirstPhysicalKeyAfterClosureDropsTheInFlightVerdict()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", '.', true);
        coord.NotePhysicalKey(
            new Keystroke(KeystrokeKind.Text, "x", 0), hasPartialWord: false);
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void NonEmptyPartialAtDeliveryDropsTheVerdict()
    {
        string partial = "";
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => partial);

        coord.OnWordCommitted("la", '.', true);
        partial = "x";
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void ClosedSentenceResolvesMultipleSlotsOneAtATime()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("la", '.', true);

        Assert.Equal(2, lane.Submitted.Count);
        Assert.Equal(2, inj.Calls.Count);
        Assert.Equal("là la.", inj.Calls[0].Target);
        Assert.Equal("là.", inj.Calls[1].Target);
    }

    [Fact]
    public void AppliedCallbackReceivesTheActualInjectionPlan()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        InjectionPlan observed = default;
        var coord = new SentenceRerankCoordinator(
            lane, ProbeForLa(), inj, () => "",
            onApplied: (_, plan) => observed = plan);

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("mer", '.', true);

        Assert.Equal(6, observed.Backspaces);
        Assert.Equal("à mer.", observed.Text);
    }

    private sealed class FakeProbe : IAmbiguityProbe
    {
        private readonly Dictionary<string, AccentVariant[]> _map;

        public FakeProbe(Dictionary<string, AccentVariant[]> map) => _map = map;

        public IReadOnlyList<AccentVariant> AmbiguousCandidates(string word) =>
            Candidates(word, includeTypedLiteral: false);

        public IReadOnlyList<AccentVariant> SentenceCandidates(
            string word, bool includeTypedLiteral) =>
            Candidates(word, includeTypedLiteral);

        private IReadOnlyList<AccentVariant> Candidates(string word, bool includeTypedLiteral)
        {
            string lower = word.ToLowerInvariant();
            var candidates = _map.TryGetValue(lower, out AccentVariant[]? mapped)
                ? new List<AccentVariant>(mapped)
                : new List<AccentVariant>();
            if (includeTypedLiteral && candidates.TrueForAll(candidate => candidate.Form != lower))
                candidates.Add(new AccentVariant(lower, 0.0));
            return candidates.Count >= 2 ? candidates : System.Array.Empty<AccentVariant>();
        }
    }

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
                : new RerankOutcome(
                    chosen, System.Array.Empty<RerankCandidateScore>(), 0.0, 0.0, null);
            ResultSink?.Invoke(new RerankResult(request.SlotIndex, request.Epoch, outcome));
        }

        public void Dispose() { }
    }
}
