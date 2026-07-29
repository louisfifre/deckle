using System.Collections.Generic;
using Deckle.Autocorrect;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

// The live second stage in isolation. These tests pin the safety contract: the
// model may judge only a closed sentence; forward typing extends the exact tail,
// while navigation/edit gestures or any unknown mutation expire the verdict.
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
    public void ForwardTextAfterClosureRemainsInTheSafeRewriteTail()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        string partial = "";
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => partial);

        coord.OnWordCommitted("la", '.', true);
        coord.NotePhysicalKey(
            new Keystroke(KeystrokeKind.Text, "x", 0), hasPartialWord: false);
        partial = "x";
        lane.DeliverLast();

        Assert.Equal(("la.x", "là.x"), Assert.Single(inj.Calls));
    }

    [Fact]
    public void SeparatorAfterClosureIsIncludedInTheSafeRewriteTail()
    {
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", '.', true);
        coord.NotePhysicalKey(
            new Keystroke(KeystrokeKind.Text, " ", 0), hasPartialWord: false);
        lane.DeliverLast();

        Assert.Equal(("la. ", "là. "), Assert.Single(inj.Calls));
    }

    [Fact]
    public void FailedFirstSlotRewriteDoesNotAttemptASecondSlot()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector { Result = false };
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", ' ', true);
        coord.OnWordCommitted("la", '.', true);

        Assert.Single(lane.Submitted);
        Assert.Single(inj.Calls);
    }

    [Fact]
    public void NonEmptyTrackedPartialAtDeliveryRemainsInTheRewriteTail()
    {
        string partial = "";
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => partial);

        coord.OnWordCommitted("la", '.', true);
        partial = "x";
        lane.DeliverLast();

        Assert.Equal(("la.x", "là.x"), Assert.Single(inj.Calls));
    }

    [Fact]
    public void RewriteExpiresWhenForwardTypingMakesTheTailTooLong()
    {
        string partial = new('x', 257);
        var lane = new TestRerankLane { Manual = true, Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => partial);

        coord.OnWordCommitted("la", '.', true);
        lane.DeliverLast();

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void ChosenFormOutsideTheSubmittedClosedSetIsNeverInjected()
    {
        var lane = new TestRerankLane { Reranker = _ => "inventé" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("la", '.', true);

        Assert.Empty(inj.Calls);
    }

    [Fact]
    public void SentenceRewritePreservesTheCommittedWordsCasePattern()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var inj = new RecordingInjector();
        var coord = new SentenceRerankCoordinator(lane, ProbeForLa(), inj, () => "");

        coord.OnWordCommitted("La", '.', true);

        Assert.Equal(("La.", "Là."), Assert.Single(inj.Calls));
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
    public void WholeSentenceModeChoosesOneEditAcrossAllSlots()
    {
        var legacyProbe = new FakeProbe(new Dictionary<string, AccentVariant[]>());
        var wholeProbe = new FakeProbe(new Dictionary<string, AccentVariant[]>
        {
            ["une"] =
            [
                new AccentVariant("une", 100),
                new AccentVariant("un", 90),
            ],
            ["seul"] =
            [
                new AccentVariant("seul", 100),
                new AccentVariant("seule", 90),
            ],
        });
        var lane = new TestRerankLane
        {
            WholeSentenceReranker = request => request.SentenceCandidates!
                .Single(candidate =>
                    candidate.SlotIndex == 4 && candidate.Form == "seule"),
        };
        var injector = new RecordingInjector();
        var coordinator = new SentenceRerankCoordinator(
            lane, legacyProbe, injector, () => "",
            wholeSentenceProbe: wholeProbe);

        coordinator.OnWordCommitted("il", ' ', true);
        coordinator.OnWordCommitted("y", ' ', true);
        coordinator.OnWordCommitted("a", ' ', true);
        coordinator.OnWordCommitted("une", ' ', true);
        coordinator.OnWordCommitted("seul", ' ', true);
        coordinator.OnWordCommitted("erreur", '.', true);

        RerankRequest submitted = Assert.Single(lane.Submitted);
        Assert.NotNull(submitted.SentenceCandidates);
        Assert.Contains(
            submitted.SentenceCandidates,
            candidate => candidate is { SlotIndex: 3, Form: "un" });
        Assert.Contains(
            submitted.SentenceCandidates,
            candidate => candidate is { SlotIndex: 4, Form: "seule" });
        Assert.Equal(
            ("seul erreur.", "seule erreur."),
            Assert.Single(injector.Calls));
    }

    [Fact]
    public void WholeSentenceModeNeverFallsBackToAWordCascade()
    {
        var probe = new FakeProbe(new Dictionary<string, AccentVariant[]>
        {
            ["la"] =
            [
                new AccentVariant("la", 100),
                new AccentVariant("là", 90),
            ],
        });
        var lane = new TestRerankLane
        {
            // This would rewrite every slot if the old compatibility cascade
            // were allowed to run after the global judge abstained.
            Reranker = _ => "là",
        };
        var injector = new RecordingInjector();
        var coordinator = new SentenceRerankCoordinator(
            lane, probe, injector, () => "",
            wholeSentenceProbe: probe);

        coordinator.OnWordCommitted("la", ' ', true);
        coordinator.OnWordCommitted("la", '.', true);

        Assert.Single(lane.Submitted);
        Assert.Empty(injector.Calls);
    }

    [Fact]
    public void WholeSentenceCandidatePreservesTheCommittedCasePattern()
    {
        var legacyProbe = new FakeProbe(new Dictionary<string, AccentVariant[]>());
        var wholeProbe = new FakeProbe(new Dictionary<string, AccentVariant[]>
        {
            ["La"] =
            [
                new AccentVariant("la", 100),
                new AccentVariant("là", 90),
            ],
        });
        var lane = new TestRerankLane
        {
            WholeSentenceReranker = request => request.SentenceCandidates!.Single(),
        };
        var injector = new RecordingInjector();
        var coordinator = new SentenceRerankCoordinator(
            lane, legacyProbe, injector, () => "",
            wholeSentenceProbe: wholeProbe);

        coordinator.OnWordCommitted("La", '.', true);

        Assert.Equal(
            new SentenceEditCandidate(0, "Là"),
            Assert.Single(Assert.Single(lane.Submitted).SentenceCandidates!));
        Assert.Equal(("La.", "Là."), Assert.Single(injector.Calls));
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

    [Fact]
    public void VerifiedCaretSentenceCanRestoreAClosedCorrectionWindow()
    {
        var lane = new TestRerankLane { Reranker = _ => "là" };
        var injector = new RecordingInjector();
        var coordinator = new SentenceRerankCoordinator(
            lane, ProbeForLa(), injector, () => "");
        var snapshot = new FocusedCaretText(
            "Avant. la.",
            ReachedDocumentStart: false,
            MovedCharacters: 10,
            ProcessId: 42,
            ControlType: 50004,
            NativeWindowHandle: 0,
            ForegroundWindow: 1234,
            RuntimeId: "42.1.2",
            Pattern: "text_selection");

        bool recovered = coordinator.RecoverVerifiedSentence(
            new VerifiedCaretSentence(snapshot, "la."));

        Assert.True(recovered);
        RerankRequest request = Assert.Single(lane.Submitted);
        Assert.NotNull(request.VerifiedSentence);
        Assert.Equal(("la.", "là."), Assert.Single(injector.Calls));
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
        public Func<RerankRequest, SentenceEditCandidate?>? WholeSentenceReranker;
        public bool Manual;

        public void Submit(RerankRequest request)
        {
            Submitted.Add(request);
            if (!Manual) Deliver(request);
        }

        public void DeliverLast() => Deliver(Submitted[^1]);

        private void Deliver(RerankRequest request)
        {
            if (request.SentenceCandidates is { Count: > 0 })
            {
                SentenceEditCandidate? edit = WholeSentenceReranker?.Invoke(request);
                RerankOutcome wholeOutcome = WholeSentenceReranker is null
                    ? RerankOutcome.Abstained(
                        RerankOutcome.AbstainReasons.WholeSentenceUnsupported)
                    : edit is SentenceEditCandidate chosenEdit
                        ? new RerankOutcome(
                            chosenEdit.Form,
                            System.Array.Empty<RerankCandidateScore>(),
                            1.0,
                            0.0,
                            null)
                        {
                            ChosenSlotIndex = chosenEdit.SlotIndex,
                        }
                        : new RerankOutcome(
                            null,
                            System.Array.Empty<RerankCandidateScore>(),
                            1.0,
                            0.0,
                            null);
                ResultSink?.Invoke(new RerankResult(
                    request.SlotIndex, request.Epoch, wholeOutcome));
                return;
            }

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
