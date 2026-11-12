using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// Threshold 20 throughout, so the hard cap is 40.
[Trait("Category", "unit")]
public sealed class DictationBatchRewriterTests
{
    static readonly RewriteProfile Profile = new() { Id = "test", Name = "Test" };

    // A rewriter whose streaming step is reached from the start.
    static DictationBatchRewriter Streaming(IRewriteService service)
    {
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Profile);
        return rewriter;
    }

    [Fact]
    public void AssembledTextIsTheRewritesInOrderWithTheJoinRule()
    {
        var rewriter = Streaming(new FakeRewriteService(text => $"  {text.ToUpperInvariant()}  "));

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
        var rewriter = Streaming(new FakeRewriteService(text => text.StartsWith("Brut") ? "  " : text.ToUpperInvariant()));

        rewriter.Push("Brut texte qui reste.", endedOnSilence: true);
        rewriter.Push("Suite.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Brut texte qui reste.\n\nSUITE.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public void ThrowingRewriteFallsBackToTheRawBatchText()
    {
        var rewriter = Streaming(new FakeRewriteService(_ => throw new InvalidOperationException()));

        rewriter.Push("Texte gardé tel quel.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Texte gardé tel quel.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    [Fact]
    public void MetricsAreSummedOverTheBatches()
    {
        var service = new FakeRewriteService(text => text, delayMs: 20);
        var rewriter = Streaming(service);

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
        var rewriter = Streaming(new FakeRewriteService(text => { gate.Wait(); return text.ToUpperInvariant(); }));
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
        var rewriter = Streaming(new FakeRewriteService(_ => "  "));
        int returned = 0;
        rewriter.BatchReturned += () => Interlocked.Increment(ref returned);

        rewriter.Push("Brut texte qui reste.", endedOnSilence: true);
        rewriter.Assemble();

        Assert.Equal("Brut texte qui reste.", rewriter.Snapshot());
        // Nothing changed in what the take reads, so no surface is told.
        Assert.Equal(0, Volatile.Read(ref returned));
    }

    [Fact]
    public void CompleteIsIdempotent()
    {
        var service = new FakeRewriteService(text => text);
        var rewriter = Streaming(service);
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
        var rewriter = Streaming(new FakeRewriteService(text => text.ToUpperInvariant()));
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
        var rewriter = Streaming(service);

        rewriter.Push("Reste.", endedOnSilence: false);
        rewriter.Assemble();

        Assert.Equal(["Reste."], service.Plain);
        Assert.Empty(service.Cached);
    }

    [Fact]
    public void ATakeOfSeveralBatchesKeepsThePromptInCacheForEachOfThem()
    {
        var service = new CacheAwareRewriteService();
        var rewriter = Streaming(service);

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
        var rewriter = Streaming(service);

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
        var rewriter = Streaming(service);

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
        var rewriter = Streaming(service);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Première phrase ici.\n\nDEUXIÈME PHRASE ICI.", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    // ── Generations: a higher streaming step rewrites the beginning again ───

    static readonly RewriteProfile Light = new() { Id = "light", Name = "Light" };
    static readonly RewriteProfile Strong = new() { Id = "strong", Name = "Strong" };

    [Fact]
    public void BeforeAStreamingStepPassagesCloseAndNothingLeaves()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Suite ouverte", endedOnSilence: false);

        Assert.Empty(service.Calls);
        Assert.Equal(1, rewriter.BatchCount);
        Assert.Equal("Première phrase ici.\n\nSuite ouverte", rewriter.Snapshot());
    }

    [Fact]
    public void ReachingAStreamingStepSendsThePassagesAlreadyClosedFromTheirRawText()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);

        rewriter.Use(Light);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Light(Première phrase ici.)\n\nLight(Deuxième phrase ici.)", result.Text);
        Assert.Equal(0, result.FallbackCount);
    }

    [Fact]
    public void AHigherStepRewritesEveryPassageAgainFromItsRawTextNeverFromARewrite()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        Assert.True(SpinWait.SpinUntil(() => service.Calls.Length == 2, TimeSpan.FromSeconds(5)));

        rewriter.Use(Strong);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Strong(Première phrase ici.)\n\nStrong(Deuxième phrase ici.)", result.Text);
        Assert.All(service.Calls, call => Assert.DoesNotContain("(", call.Input));
        Assert.Equal(0, result.FallbackCount);
    }

    // Panel 3: a passage keeps its earlier reading until its new one arrives,
    // and never goes back to the raw text.
    [Fact]
    public void APassageKeepsItsEarlierReadingUntilTheNewOneArrives()
    {
        using var strongAnswers = new ManualResetEventSlim();
        var service = new ProfileRewriteService(hold: profile => profile.Id == "strong" ? strongAnswers : null);
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        Assert.True(SpinWait.SpinUntil(
            () => rewriter.Snapshot() == "Light(Première phrase ici.)", TimeSpan.FromSeconds(5)));

        rewriter.Use(Strong);
        Assert.True(SpinWait.SpinUntil(() => service.Calls.Length == 2, TimeSpan.FromSeconds(5)));
        Assert.Equal("Light(Première phrase ici.)", rewriter.Snapshot());

        strongAnswers.Set();
        Assert.Equal("Strong(Première phrase ici.)", rewriter.Assemble().Text);
    }

    // An answer of an earlier profile that comes back after the later one is
    // dropped: the passage only moves forward.
    [Fact]
    public void AnAnswerOfAnEarlierProfileArrivingLateIsIgnored()
    {
        using var lightAnswers = new ManualResetEventSlim();
        var service = new ProfileRewriteService(hold: profile => profile.Id == "light" ? lightAnswers : null);
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);

        rewriter.Use(Strong);
        Assert.Equal("Strong(Première phrase ici.)", rewriter.Assemble().Text);

        lightAnswers.Set();
        Assert.True(SpinWait.SpinUntil(() => service.Answered == 2, TimeSpan.FromSeconds(5)));
        Thread.Sleep(50);
        Assert.Equal("Strong(Première phrase ici.)", rewriter.Snapshot());
    }

    // A failure of the later profile leaves the reading the passage had.
    [Fact]
    public void AFailedRewriteAtAHigherStepKeepsTheEarlierReading()
    {
        var service = new ProfileRewriteService(fail: profile => profile.Id == "strong");
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        Assert.True(SpinWait.SpinUntil(
            () => rewriter.Snapshot() == "Light(Première phrase ici.)", TimeSpan.FromSeconds(5)));

        rewriter.Use(Strong);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Light(Première phrase ici.)", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    // The burst that rewrites the beginning again lets one request leave
    // first, for the prompt cache, and the others after its answer.
    [Fact]
    public void TheBurstOfAHigherStepLetsOneRequestLeaveFirst()
    {
        using var firstStrong = new ManualResetEventSlim();
        var service = new ProfileRewriteService(holdFirstOf: "strong", holdUntil: firstStrong);
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        rewriter.Push("Troisième phrase là.", endedOnSilence: true);

        rewriter.Use(Strong);
        Assert.True(SpinWait.SpinUntil(() => service.Calls.Length == 1, TimeSpan.FromSeconds(5)));
        Thread.Sleep(150);
        Assert.Single(service.Calls);

        firstStrong.Set();
        rewriter.Assemble();
        Assert.Equal(3, service.Calls.Length);
    }

    [Fact]
    public void UsingTheSameProfileAgainSendsNothing()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);

        rewriter.Use(Light);
        rewriter.Assemble();

        Assert.Single(service.Calls);
    }

    // ── Blocks per step: a step that takes over cuts in its own size ────────

    // Behaviours 4: the step that takes over cuts what was said into its own
    // blocks, whole blocks of the step before joined until they reach its
    // size, the rest as a last, shorter block.
    [Fact]
    public void AStepWithLargerBlocksJoinsTheClosedBlocksIntoItsOwn()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        rewriter.Push("Troisième phrase là.", endedOnSilence: true);

        rewriter.Use(Strong, blockChars: 40);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal(
            ["Première phrase ici.\n\nDeuxième phrase ici.", "Troisième phrase là."],
            service.Calls.Select(call => call.Input).Order());
        Assert.Equal(
            "Strong(Première phrase ici.\n\nDeuxième phrase ici.)\n\nStrong(Troisième phrase là.)",
            result.Text);
        Assert.Equal(2, result.BatchCount);
        Assert.Equal(0, result.FallbackCount);
    }

    // Panel 3: a block a later step joined into a larger one keeps the
    // reading it had until the new one of its group arrives, and never goes
    // back to the raw text.
    [Fact]
    public void JoinedBlocksKeepTheirEarlierReadingsUntilTheirGroupsArrives()
    {
        using var strongAnswers = new ManualResetEventSlim();
        var service = new ProfileRewriteService(hold: profile => profile.Id == "strong" ? strongAnswers : null);
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        const string light = "Light(Première phrase ici.)\n\nLight(Deuxième phrase ici.)";
        Assert.True(SpinWait.SpinUntil(() => rewriter.Snapshot() == light, TimeSpan.FromSeconds(5)));

        rewriter.Use(Strong, blockChars: 40);
        Assert.True(SpinWait.SpinUntil(() => service.Calls.Length == 3, TimeSpan.FromSeconds(5)));
        Assert.Equal(light, rewriter.Snapshot());

        strongAnswers.Set();
        Assert.Equal("Strong(Première phrase ici.\n\nDeuxième phrase ici.)", rewriter.Assemble().Text);
    }

    // Panel 4: a group whose rewrite fails shows its blocks' earlier readings.
    [Fact]
    public void AJoinedBlockWhoseRewriteFailsKeepsItsBlocksReadings()
    {
        var service = new ProfileRewriteService(fail: profile => profile.Id == "strong");
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        Assert.True(SpinWait.SpinUntil(() => service.Answered == 2, TimeSpan.FromSeconds(5)));

        rewriter.Use(Strong, blockChars: 40);
        DictationBatchResult result = rewriter.Assemble();

        Assert.Equal("Light(Première phrase ici.)\n\nLight(Deuxième phrase ici.)", result.Text);
        Assert.Equal(1, result.FallbackCount);
    }

    // Behaviours 5: once a step takes over, the open block closes at its
    // size, not at the size of the step before.
    [Fact]
    public void AfterATakeOverTheOpenBlockClosesAtTheNewSize()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Strong, blockChars: 40);

        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        Assert.Equal(0, rewriter.BatchCount);

        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);
        Assert.Equal(1, rewriter.BatchCount);
    }

    // The same profile at another size is another step: it cuts again.
    [Fact]
    public void TheSameProfileAtAnotherBlockSizeSendsAgain()
    {
        var service = new ProfileRewriteService();
        var rewriter = new DictationBatchRewriter(service, 20);
        rewriter.Use(Light);
        rewriter.Push("Première phrase ici.", endedOnSilence: true);
        rewriter.Push("Deuxième phrase ici.", endedOnSilence: true);

        rewriter.Use(Light, blockChars: 40);
        rewriter.Assemble();

        Assert.Equal(3, service.Calls.Length);
    }

    // Answers "<profile name>(<text>)", records each call, and can hold or
    // fail the calls of a profile.
    private sealed class ProfileRewriteService(
        Func<RewriteProfile, ManualResetEventSlim?>? hold = null,
        Func<RewriteProfile, bool>? fail = null,
        string? holdFirstOf = null,
        ManualResetEventSlim? holdUntil = null) : IRewriteService
    {
        readonly object _lock = new();
        readonly List<(string ProfileId, string Input)> _calls = new();
        int _answered;

        public (string ProfileId, string Input)[] Calls { get { lock (_lock) return _calls.ToArray(); } }
        public int Answered => Volatile.Read(ref _answered);

        public GenerationResult Rewrite(string text, RewriteProfile profile)
        {
            bool firstOfHeld;
            lock (_lock)
            {
                firstOfHeld = profile.Id == holdFirstOf && !_calls.Any(c => c.ProfileId == holdFirstOf);
                _calls.Add((profile.Id, text));
            }
            try
            {
                if (firstOfHeld) holdUntil?.Wait(TimeSpan.FromSeconds(10));
                hold?.Invoke(profile)?.Wait(TimeSpan.FromSeconds(10));
                if (fail?.Invoke(profile) == true) return new GenerationResult(null, 1, 0, 0, 0, 0, 0);
                return new GenerationResult($"{profile.Name}({text})", 1, 0, 0, 0, 0, 0);
            }
            finally
            {
                Interlocked.Increment(ref _answered);
            }
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken)
            => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken)
            => default;
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
