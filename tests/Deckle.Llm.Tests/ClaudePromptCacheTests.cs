using Anthropic.Models.Messages;
using Xunit;

namespace Deckle.Llm.Tests;

// Where the cache marker sits in the request the Messages API receives, for
// each scope. The marker's place decides what the next call can read back: on
// the system prompt's block the prompt alone, at the top level the prompt and
// the text together.
[Trait("Category", "unit")]
public sealed class ClaudePromptCacheTests
{
    static MessageCreateParams Build(PromptCacheScope scope)
    {
        var request = new GenerationRequest(
            Model: "claude-sonnet-5-5", SystemPrompt: "prompt", UserText: "text", Label: "test", Cache: scope);
        return ClaudeEngine.BuildParameters(request, ClaudeEngine.ReasoningFor(request.Model, request.Reasoning));
    }

    [Fact]
    public void WithoutAScopeTheRequestCarriesNoMarker()
    {
        MessageCreateParams parameters = Build(PromptCacheScope.None);

        Assert.Null(parameters.CacheControl);
        Assert.True(parameters.System!.TryPickString(out string? prompt));
        Assert.Equal("prompt", prompt);
    }

    [Fact]
    public void TheSystemPromptScopeMarksThePromptBlockAndNothingElse()
    {
        MessageCreateParams parameters = Build(PromptCacheScope.SystemPrompt);

        Assert.Null(parameters.CacheControl);
        Assert.True(parameters.System!.TryPickTextBlockParams(out IReadOnlyList<TextBlockParam>? blocks));
        TextBlockParam block = Assert.Single(blocks!);
        Assert.Equal("prompt", block.Text);
        Assert.NotNull(block.CacheControl);
    }

    [Fact]
    public void TheWholeRequestScopeMarksTheRequestAndLeavesThePromptAsText()
    {
        MessageCreateParams parameters = Build(PromptCacheScope.WholeRequest);

        Assert.NotNull(parameters.CacheControl);
        Assert.True(parameters.System!.TryPickString(out _));
    }
}
