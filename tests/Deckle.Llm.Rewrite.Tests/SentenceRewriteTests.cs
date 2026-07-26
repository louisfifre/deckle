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
        var service = new RewriteService(engine);

        RewriteResult result = service.RewriteSentence(
            "Il faut prepare le terrain.",
            "http://localhost:11434",
            TestContext.Current.CancellationToken);

        Assert.Equal("Il faut préparer le terrain.", result.Text);
        Assert.Equal("http://localhost:11434", engine.Request.Endpoint);
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
        var service = new RewriteService(new CancellingEngine());

        RewriteResult result = service.RewriteSentence(
            "Phrase.",
            "local",
            cancellation.Token);

        Assert.Null(result.Text);
    }

    private sealed class CapturingEngine : IRewriteEngine
    {
        public RewriteEngineRequest Request { get; private set; }

        public RewriteResult Generate(
            RewriteEngineRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return new RewriteResult("Il faut préparer le terrain.", 1, 0, 0, 0, 0, 0);
        }
    }

    private sealed class CancellingEngine : IRewriteEngine
    {
        public RewriteResult Generate(
            RewriteEngineRequest request,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);
    }
}
