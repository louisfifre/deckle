using Anthropic.Models.Messages;
using Xunit;

namespace Deckle.Llm.Tests;

// What the Messages API is sent for each reasoning level. Turning thinking off
// takes a different field on each line of models, so Off is checked per model.
[Trait("Category", "unit")]
public sealed class ClaudeReasoningTests
{
    [Fact]
    public void NoLevelKeepsLowEffortAndAdaptiveThinking()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-sonnet-5-5", null);

        Assert.Equal(Effort.Low, reasoning.Effort);
        Assert.Null(reasoning.Thinking);
    }

    [Theory]
    [InlineData(ReasoningLevel.Low, Effort.Low)]
    [InlineData(ReasoningLevel.Medium, Effort.Medium)]
    [InlineData(ReasoningLevel.High, Effort.High)]
    public void ALevelSetsTheMatchingEffortWithAdaptiveThinking(ReasoningLevel level, Effort effort)
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-haiku-5-5", level);

        Assert.Equal(effort, reasoning.Effort);
        Assert.Null(reasoning.Thinking);
    }

    [Fact]
    public void OffDisablesThinkingOnHaiku()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-haiku-5-5", ReasoningLevel.Off);

        Assert.True(reasoning.Thinking!.TryPickDisabled(out _));
        Assert.Equal(Effort.Low, reasoning.Effort);
    }

    [Fact]
    public void OffUsesBetweenToolsOnSonnet55WhichRejectsDisabled()
    {
        var reasoning = ClaudeEngine.ReasoningFor("claude-sonnet-5-5", ReasoningLevel.Off);

        Assert.True(reasoning.Thinking!.TryPickBetweenTools(out _));
        Assert.Equal(Effort.Low, reasoning.Effort);
    }

    [Theory]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-fable-5-1")]
    public void OffFallsBackToLowEffortOnModelsThatAlwaysThink(string model)
    {
        var reasoning = ClaudeEngine.ReasoningFor(model, ReasoningLevel.Off);

        Assert.Null(reasoning.Thinking);
        Assert.Equal(Effort.Low, reasoning.Effort);
        Assert.Contains("off_unsupported", reasoning.Detail);
    }
}
