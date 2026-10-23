using System.Diagnostics.Tracing;
using Deckle.Autocorrect;
using Deckle.Core;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
[Collection(AutocorrectObservabilityCollection.Name)]
public sealed class BackgroundRerankLaneTests
{
    [Fact]
    public async Task ChangedRecoveredSentenceTurnsAnApplyingVerdictStale()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string literal = "Il y a une seul erreur.";
        var reader = new MutableCaretReader(literal);
        var reranker = new BlockingWholeSentenceReranker();
        var host = new FakeKeyboardInputHost { DeferDrain = true };
        using var lane = new BackgroundRerankLane(reranker, host, reader);
        RerankResult? delivered = null;
        lane.ResultSink = result => delivered = result;
        var verified = new VerifiedCaretSentence(reader.Current(), literal);
        lane.Submit(new ClosedSentenceRerankRequest(
            new ClosedSentenceTransaction(
                literal,
                new[] { "Il", "y", "a", "une", "seul", "erreur" },
                [new SentenceEditCandidate(4, 11, 4, "seule")]),
            Epoch: 1,
            VerifiedSentence: verified));

        await reranker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        reader.Text = "Il y a une seule erreur.";
        reranker.Release.SetResult();
        Assert.True(SpinWait.SpinUntil(() => host.HasPendingDrain, 2_000));
        host.Drain();

        RerankResult result = Assert.IsType<RerankResult>(delivered);
        Assert.Null(result.Outcome.Chosen);
        Assert.Equal(
            RerankOutcome.AbstainReasons.StaleEvidence,
            result.Outcome.AbstainReason);
    }

    [Fact]
    public async Task DisposeWaitsForNativeInferenceBeforeReleasingTheReranker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var reranker = new BlockingReranker();
        var host = new FakeKeyboardInputHost { DeferDrain = true };
        var lane = new BackgroundRerankLane(reranker, host);
        lane.Submit(new HistoricalSlotRerankRequest(
            new[] { "une", "phrase", "assez", "longue" },
            SlotIndex: 3,
            Candidates: [
                new AccentVariant("longue", 1),
                new AccentVariant("longues", 1),
            ],
            Epoch: 1));

        await reranker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Task disposal = Task.Run(lane.Dispose, cancellationToken);

        // "Still blocked" is a negative, so it only means something once the
        // disposal is really running: a task still queued behind a starved
        // pool would also look blocked. After this, a Dispose that did not
        // wait would be done within microseconds, well inside the delay.
        Assert.True(SpinWait.SpinUntil(
            () => disposal.Status != TaskStatus.WaitingToRun, 2_000));
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.False(disposal.IsCompleted);
        Assert.False(reranker.Disposed);

        reranker.Release.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.True(reranker.Disposed);
    }

    // Regression: the worker's bare catch turned a throwing judge into a silent
    // Error abstain, so a judge failing on every sentence left no trace of why.
    [Fact]
    public async Task ThrowingRerankerAbstainsAndReportsItsCauseOnceUntilRecovery()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string cause = "lane-regression: judge session lost";
        using var listener = new TestEventListener("Deckle-Autocorrect");
        var reranker = new ThrowOnceReranker(cause);
        var host = new FakeKeyboardInputHost { DeferDrain = true };
        var lane = new BackgroundRerankLane(reranker, host);
        var delivered = new List<RerankResult>();
        lane.ResultSink = delivered.Add;

        lane.Submit(SlotRequest(epoch: 1));
        Assert.True(SpinWait.SpinUntil(() => host.HasPendingDrain, 2_000));
        host.Drain();
        lane.Submit(SlotRequest(epoch: 2));
        Assert.True(SpinWait.SpinUntil(() => host.HasPendingDrain, 2_000));
        host.Drain();
        await Task.Run(lane.Dispose, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.Equal(2, delivered.Count);
        Assert.Equal(RerankOutcome.AbstainReasons.Error, delivered[0].Outcome.AbstainReason);
        Assert.Equal(RerankOutcome.AbstainReasons.NoRule, delivered[1].Outcome.AbstainReason);
        EventWrittenEventArgs failing = Assert.Single(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtRerankFailing
            && PayloadValue(e, "ex_message") is cause);
        Assert.Equal(
            DeckleAutocorrectSource.RerankFailureSources.Inference,
            PayloadValue(failing, "source"));
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            PayloadValue(failing, "ex_type"));
        Assert.Contains(listener.Events, e =>
            e.EventId == DeckleAutocorrectSource.EvtRerankRecovered
            && PayloadValue(e, "failures") is 1);
    }

    private static HistoricalSlotRerankRequest SlotRequest(int epoch) => new(
        new[] { "une", "phrase", "assez", "longue" },
        SlotIndex: 3,
        Candidates: [
            new AccentVariant("longue", 1),
            new AccentVariant("longues", 1),
        ],
        Epoch: epoch);

    private static object? PayloadValue(EventWrittenEventArgs ev, string name)
    {
        var names = ev.PayloadNames;
        var payload = ev.Payload;
        if (names is null || payload is null) return null;
        for (int i = 0; i < names.Count; i++)
            if (names[i] == name) return payload[i];
        return null;
    }

    private sealed class ThrowOnceReranker(string message) : ISentenceReranker
    {
        private int _calls;

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException(message);
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
        }
    }

    private sealed class BlockingReranker : ISentenceReranker, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates)
        {
            Entered.SetResult();
            Release.Task.GetAwaiter().GetResult();
            return RerankOutcome.Abstained(RerankOutcome.AbstainReasons.NoRule);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class BlockingWholeSentenceReranker
        : ISentenceReranker, IWholeSentenceReranker
    {
        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RerankOutcome Rerank(
            IReadOnlyList<string> sentence,
            int slotIndex,
            IReadOnlyList<AccentVariant> candidates) =>
            throw new InvalidOperationException("Expected one global sentence verdict.");

        public RerankOutcome RerankSentence(ClosedSentenceTransaction transaction)
        {
            Entered.SetResult();
            Release.Task.GetAwaiter().GetResult();
            SentenceEditCandidate winner = Assert.Single(transaction.Edits);
            return new RerankOutcome(
                winner.Replacement,
                Array.Empty<RerankCandidateScore>(),
                Margin: 2.0,
                Threshold: 1.0,
                AbstainReason: null)
            {
                ChosenSlotIndex = winner.SlotIndex,
            };
        }
    }

    private sealed class MutableCaretReader(string text) : ICaretTextReader
    {
        public string Text { get; set; } = text;

        public FocusedCaretText Current() => new(
            Text,
            ReachedDocumentStart: true,
            MovedCharacters: Text.Length,
            ProcessId: 42,
            ControlType: 50004,
            NativeWindowHandle: 0,
            ForegroundWindow: 1234,
            RuntimeId: "42.1.2",
            Pattern: "text_selection");

        public bool TryReadStable(out FocusedCaretText text, out string reason)
        {
            text = Current();
            reason = CaretTextReadReasons.Accepted;
            return true;
        }
    }
}
