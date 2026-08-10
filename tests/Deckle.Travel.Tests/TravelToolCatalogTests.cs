using Deckle.Anytype.Mcp;
using Deckle.Travel;
using Xunit;

namespace Deckle.Travel.Tests;

[Trait("Category", "unit")]
public class TravelToolCatalogTests
{
    [Fact]
    public void EveryToolDeclaresItsAmbiguousOutcomePolicy()
    {
        IReadOnlyList<ToolDescriptor> tools = TravelToolCatalog.Build(
            () => throw new InvalidOperationException("handlers are not invoked"));
        var expected = new Dictionary<string, AmbiguousOutcomePolicy>(StringComparer.Ordinal)
        {
            ["create"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["update"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["attach"] = AmbiguousOutcomePolicy.RequiresDeduplication,
            ["get"] = AmbiguousOutcomePolicy.SafeToRetry,
            ["search"] = AmbiguousOutcomePolicy.SafeToRetry,
        };

        Assert.Equal(expected, tools.ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.AmbiguousOutcome,
            StringComparer.Ordinal));
        Assert.True(tools.Single(tool => tool.Name == "update").Execution.RequiresStableTarget);
        var expectedChanges = new Dictionary<string, ToolChangeKind>(StringComparer.Ordinal)
        {
            ["create"] = ToolChangeKind.Additive,
            ["update"] = ToolChangeKind.Overwriting,
            ["attach"] = ToolChangeKind.Additive,
            ["get"] = ToolChangeKind.None,
            ["search"] = ToolChangeKind.None,
        };
        Assert.Equal(expectedChanges, tools.ToDictionary(
            tool => tool.Name,
            tool => tool.Execution.Change,
            StringComparer.Ordinal));
    }
}
