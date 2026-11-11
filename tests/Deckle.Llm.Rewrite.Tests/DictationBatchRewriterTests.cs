using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Threshold 20 throughout, so the hard cap is 40.
[Trait("Category", "unit")]
public sealed class DictationBatchRewriterTests
{
    static readonly RewriteProfile Profile = new() { Name = "Test" };

    [Fact]
    public void AssembledTextIsTheRewritesInOrderWithTheJoinRule()
    {
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(text => $"  {text.ToUpperInvariant()}  "), Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);                         // closes on the pause
        rewriter.Push("Deuxième phrase assez longue pour le cap.", endedOnSilence: false);   // closes on the cap
        rewriter.Push("Fin.", endedOnSilence: true);                                         // flushed
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal(
            "PREMIÈRE PHRASE ICI.\n\nDEUXIÈME PHRASE ASSEZ LONGUE POUR LE CAP. FIN.",
            result.Text);
        Assert.Equal(3, result.BatchCount);
        Assert.Equal(0, result.FallbackCount);
    }

    [Fact]
    public void BlankRewriteFallsBackToTheRawBatchText()
    {
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(text => text.StartsWith("Brut") ? "  " : text.ToUpperInvariant()),
            Profile, 20);

        rewriter.Push("Brut texte qui reste.", endedOnSilence: true);
        rewriter.Push("Suite.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Brut texte qui reste.\n\nSUITE.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public void ThrowingRewriteFallsBackToTheRawBatchText()
    {
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(_ => throw new InvalidOperationException()), Profile, 20);

        rewriter.Push("Texte gardé tel quel.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Texte gardé tel quel.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public void MetricsAreSummedOverTheBatches()
    {
        var service = new FakeRewriteService(text => text, delayMs: 20);
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Seconde.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal(2, result.BatchCount);
        Assert.True(result.LlmMs >= 30, $"LlmMs = {result.LlmMs}");
        Assert.Equal(2, result.LlmLoadMs);
        Assert.Equal(4, result.LlmPromptEvalMs);
        Assert.Equal(6, result.LlmEvalMs);
        Assert.Equal(8, result.LlmPromptTokens);
        Assert.Equal(10, result.LlmEvalTokens);
        Assert.Equal(12, result.LlmCacheWriteTokens);
        Assert.Equal(14, result.LlmCacheReadTokens);
    }

    [Fact]
    public void SnapshotShowsTheOpenTextRaw_ThenEachBatchRewrittenAsItReturns()
    {
        var gate = new ManualResetEventSlim();
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(text => { gate.Wait(); return text.ToUpperInvariant(); }),
            Profile, 20);
        var returned = new ManualResetEventSlim();
        rewriter.BatchReturned += returned.Set;

        rewriter.Push("Première phrase ici.", endedOnSilence: true);   // closes on the pause
        rewriter.Push("Suite ouverte", endedOnSilence: false);          // stays open

        // The rewrite is still running: the closed batch reads raw.
        Assert.Equal("Première phrase ici.\n\nSuite ouverte", rewriter.Snapshot());

        gate.Set();
        Assert.True(returned.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal("PREMIÈRE PHRASE ICI.\n\nSuite ouverte", rewriter.Snapshot());

        // The final assembly is the last snapshot.
        Assert.Equal("PREMIÈRE PHRASE ICI.\n\nSUITE OUVERTE", rewriter.Assemble().Text);
    }

    [Fact]
    public void SnapshotKeepsTheRawTextOfABatchThatFellBack()
    {
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(_ => "  "), Profile, 20);
        var returned = new ManualResetEventSlim();
        rewriter.BatchReturned += returned.Set;

        rewriter.Push("Brut texte qui reste.", endedOnSilence: true);
        Assert.True(returned.Wait(TimeSpan.FromSeconds(5)));

        Assert.Equal("Brut texte qui reste.", rewriter.Snapshot());
    }

    [Fact]
    public void CompleteIsIdempotent()
    {
        var service = new FakeRewriteService(text => text);
        var rewriter = new DictationBatchRewriter(service, Profile, 20);
        rewriter.Push("Reste.", endedOnSilence: false);

        rewriter.Complete();
        rewriter.Complete();
        rewriter.Assemble();

        Assert.Equal(1, rewriter.BatchCount);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public void AssembleCompletesTheOpenBatch()
    {
        var rewriter = new DictationBatchRewriter(
            new FakeRewriteService(text => text.ToUpperInvariant()), Profile, 20);
        rewriter.Push("Reste.", endedOnSilence: false);

        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("RESTE.", result.Text);
        Assert.Equal(1, result.BatchCount);
    }

    // ── The provider's prompt cache ─────────────────────────────────────────

    [Fact]
    public void ATakeOfOneBatchIsSentWithoutThePromptCache()
    {
        var service = new CacheAwareRewriteService();
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Reste.", endedOnSilence: false);
        rewriter.Assemble();

        Assert.Equal(["Reste."], service.Plain);
        Assert.Empty(service.Cached);
    }

    [Fact]
    public void ATakeOfSeveralBatchesKeepsThePromptInCacheForEachOfThem()
    {
        var service = new CacheAwareRewriteService();
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);   // closes on the pause
        rewriter.Push("Fin.", endedOnSilence: true);                    // flushed
        rewriter.Assemble();

        Assert.Equal(["Fin.", "Première phrase ici."], service.Cached.Order());
        Assert.Empty(service.Plain);
    }

    [Fact]
    public void BatchesClosedWhileTheFirstIsOutWaitForItsAnswer()
    {
        using var firstAnswer = new ManualResetEventSlim();
        var service = new CacheAwareRewriteService(holdFirstCallUntil: firstAnswer);
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        rewriter.Push("Troisième phrase là.", endedOnSilence: true);

        Assert.True(SpinWait.SpinUntil(() => service.Started == 1, TimeSpan.FromSeconds(5)));
        // Long enough for a batch that did not wait to have reached the service.
        Thread.Sleep(150);
        Assert.Equal(1, service.Started);

        firstAnswer.Set();
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal(3, service.Started);
        Assert.Equal(
            "PREMIÈRE PHRASE ICI.\n\nDEUXIÈME PHRASE ICI.\n\nTROISIÈME PHRASE LÀ.", result.Text);
    }

    [Fact]
    public void TheBatchFlushedAtStopLeavesWithoutWaitingForTheFirst()
    {
        using var firstAnswer = new ManualResetEventSlim();
        var service = new CacheAwareRewriteService(holdFirstCallUntil: firstAnswer);
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Fin.", endedOnSilence: false);
        Assert.True(SpinWait.SpinUntil(() => service.Started == 1, TimeSpan.FromSeconds(5)));

        rewriter.Complete();

        // The first batch has not answered, and the last one is out already.
        Assert.True(SpinWait.SpinUntil(() => service.Started == 2, TimeSpan.FromSeconds(5)));

        firstAnswer.Set();
        Assert.Equal("PREMIÈRE PHRASE ICI.\n\nFIN.", rewriter.Assemble().Text);
    }

    [Fact]
    public void AFirstBatchThatFailsDoesNotHoldTheOthersBack()
    {
        var service = new CacheAwareRewriteService(failFirstCall: true);
        var rewriter = new DictationBatchRewriter(service, Profile, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Première phrase ici.\n\nDEUXIÈME PHRASE ICI.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    // Tells the two ways a batch can be sent apart, and can hold or fail the
    // first call so a test decides when the cache writer answers.
    private sealed class CacheAwareRewriteService(
        ManualResetEventSlim? holdFirstCallUntil = null, bool failFirstCall = false) : IRewriteService
    {
        readonly object _lock = new();
        readonly List<string> _plain = new();
        readonly List<string> _cached = new();
        int _started;

        public int Started => Volatile.Read(ref _started);
        public string[] Plain { get { lock (_lock) return _plain.ToArray(); } }
        public string[] Cached { get { lock (_lock) return _cached.ToArray(); } }

        public GenerationResult Rewrite(string text, RewriteProfile profile) => Answer(text, _plain);

        public GenerationResult RewriteBatch(string text, RewriteProfile profile) => Answer(text, _cached);

        GenerationResult Answer(string text, List<string> sentAs)
        {
            lock (_lock) sentAs.Add(text);
            bool first = Interlocked.Increment(ref _started) == 1;
            if (first) holdFirstCallUntil?.Wait(TimeSpan.FromSeconds(10));
            if (first && failFirstCall) throw new InvalidOperationException("first batch failed");
            return new GenerationResult(text.ToUpperInvariant(), 10, 1, 2, 3, 4, 5);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken)
            => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken)
            => default;
    }

    private sealed class FakeRewriteService(Func<string, string?> rewrite, int delayMs = 0) : IRewriteService
    {
        int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public GenerationResult Rewrite(string text, RewriteProfile profile)
        {
            Interlocked.Increment(ref _calls);
            if (delayMs > 0) Thread.Sleep(delayMs);
            return new GenerationResult(rewrite(text), 10, 1, 2, 3, 4, 5, CacheWriteTokens: 6, CacheReadTokens: 7);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken)
            => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken)
            => default;
    }
}
