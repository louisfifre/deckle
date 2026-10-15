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

    private sealed class FakeRewriteService(Func<string, string?> rewrite, int delayMs = 0) : IRewriteService
    {
        int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public GenerationResult Rewrite(string text, RewriteProfile profile)
        {
            Interlocked.Increment(ref _calls);
            if (delayMs > 0) Thread.Sleep(delayMs);
            return new GenerationResult(rewrite(text), 10, 1, 2, 3, 4, 5);
        }

        public GenerationResult RewriteParagraph(string paragraph, CancellationToken cancellationToken)
            => default;

        public GenerationResult RewriteSentence(string sentence, CancellationToken cancellationToken)
            => default;
    }
}
