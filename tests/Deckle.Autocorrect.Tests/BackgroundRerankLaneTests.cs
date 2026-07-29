using Deckle.Autocorrect;
using Xunit;

namespace Deckle.Autocorrect.Tests;

[Trait("Category", "unit")]
public sealed class BackgroundRerankLaneTests
{
    [Fact]
    public async Task DisposeWaitsForNativeInferenceBeforeReleasingTheReranker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var reranker = new BlockingReranker();
        var host = new FakeKeyboardInputHost { DeferDrain = true };
        var lane = new BackgroundRerankLane(reranker, host);
        lane.Submit(new RerankRequest(
            new[] { "une", "phrase", "assez", "longue" },
            SlotIndex: 3,
            new[] { new AccentVariant("longue", 1), new AccentVariant("longues", 1) },
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
}
