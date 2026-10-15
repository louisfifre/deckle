using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class SentenceRewriteTests
{
    [Fact]
    public void BuildsAZeroTemperatureMinimalCorrectionRequest()
    {
        var engine = new CapturingEngine();
        var service = new RewriteService(new SingleEngine(SentenceRewrite.ProviderId, engine));

        GenerationResult result = service.RewriteSentence(
            "Il faut prepare le terrain.",
            TestContext.Current.CancellationToken);

        Assert.Equal("Il faut préparer le terrain.", result.Text);
        Assert.Equal("Il faut prepare le terrain.", engine.Request.UserText);
        Assert.Equal(SentenceRewrite.Label, engine.Request.Label);
        Assert.Equal(SentenceRewrite.Model, engine.Request.Model);
        Assert.Equal(0, engine.Request.Temperature);
        Assert.Contains("Aucun synonyme", engine.Request.SystemPrompt);
        Assert.Contains("renvoie-la identique", engine.Request.SystemPrompt);
    }

    [Fact]
    public void CancellationReturnsNoProposal()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new RewriteService(
            new SingleEngine(SentenceRewrite.ProviderId, new CancellingEngine()));

        GenerationResult result = service.RewriteSentence(
            "Phrase.",
            cancellation.Token);

        Assert.Null(result.Text);
    }

    [Fact]
    public void MissingProviderReturnsNoProposal()
    {
        var engine = new CapturingEngine();
        var service = new RewriteService(new SingleEngine("another-provider", engine));

        GenerationResult result = service.RewriteSentence(
            "Phrase.",
            TestContext.Current.CancellationToken);

        Assert.Null(result.Text);
        Assert.Equal(default, engine.Request);
    }

    [Fact]
    public void TranscriptRewriteSendsTheProfileToItsProvider()
    {
        var engine = new CapturingEngine();
        var service = new RewriteService(new SingleEngine("claude", engine));
        var profile = new RewriteProfile
        {
            Name = "Lissage",
            ProviderId = "claude",
            Model = "claude-sonnet-5-5",
            SystemPrompt = "Lisse.",
        };

        GenerationResult result = service.Rewrite("euh bonjour", profile);

        Assert.Equal("Il faut préparer le terrain.", result.Text);
        Assert.Equal("euh bonjour", engine.Request.UserText);
        Assert.Equal("claude-sonnet-5-5", engine.Request.Model);
        Assert.Equal("Lissage", engine.Request.Label);
    }

    private sealed class SingleEngine(string providerId, IGenerationEngine engine) : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string id) => id == providerId ? engine : null;
    }

    private sealed class CapturingEngine : IGenerationEngine
    {
        public GenerationRequest Request { get; private set; }

        public GenerationResult Generate(
            GenerationRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return new GenerationResult("Il faut préparer le terrain.", 1, 0, 0, 0, 0, 0);
        }
    }

    private sealed class CancellingEngine : IGenerationEngine
    {
        public GenerationResult Generate(
            GenerationRequest request,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);
    }
}
