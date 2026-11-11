using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// What the service asks the provider to keep in its prompt cache, read on the
// request the engine receives: nothing for a whole dictation, the profile's
// prompt for a batch of one.
[Trait("Category", "unit")]
public sealed class RewriteServiceCacheTests
{
    static readonly RewriteProfile Profile = new()
    {
        Name = "Affinage", ProviderId = "claude", Model = "claude-sonnet-5-5", SystemPrompt = "prompt",
    };

    [Fact]
    public void AWholeDictationAsksForNoCache()
    {
        var engine = new CapturingEngine();
        var service = new RewriteService(new SingleEngine(engine));

        service.Rewrite("texte", Profile);

        Assert.Equal(PromptCacheScope.None, engine.Last.Cache);
    }

    [Fact]
    public void ABatchAsksToKeepTheProfilePrompt()
    {
        var engine = new CapturingEngine();
        var service = new RewriteService(new SingleEngine(engine));

        GenerationResult result = service.RewriteBatch("texte", Profile);

        Assert.Equal(PromptCacheScope.SystemPrompt, engine.Last.Cache);
        Assert.Equal("prompt", engine.Last.SystemPrompt);
        Assert.Equal("ok", result.Text);
    }

    private sealed class SingleEngine(IGenerationEngine engine) : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string id) => engine;
    }

    private sealed class CapturingEngine : IGenerationEngine
    {
        public GenerationRequest Last { get; private set; }

        public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
        {
            Last = request;
            return new GenerationResult("ok", 1, 0, 0, 1, 1, 1);
        }
    }
}
