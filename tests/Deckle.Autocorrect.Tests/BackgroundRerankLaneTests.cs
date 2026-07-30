using Deckle.Autocorrect;
using Deckle.Core;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
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

        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.False(disposal.IsCompleted);
        Assert.False(reranker.Disposed);

        reranker.Release.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.True(reranker.Disposed);
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
