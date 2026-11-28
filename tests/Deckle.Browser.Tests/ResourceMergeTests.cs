using System.Text.Json.Nodes;
using Xunit;

namespace Deckle.Browser.Tests;

// uBlock Origin's resources win over Brave's when a name or an alias collides.
[Trait("Category", "unit")]
public sealed class ResourceMergeTests
{
    private static string[] Names(string json) =>
        JsonNode.Parse(json)!.AsArray().Select(n => (string)n!["name"]!).ToArray();

    [Fact]
    public void AWinningNameDropsTheOthersEntryOfTheSameName()
    {
        string merged = ResourceMerge.Merge(
            """[{"name":"a.js","content":"U"}]""",
            """[{"name":"a.js","content":"B"},{"name":"b.js","content":"B"}]""");
        Assert.Equal(["a.js", "b.js"], Names(merged));
        Assert.Equal("U", (string)JsonNode.Parse(merged)![0]!["content"]!);
    }

    [Fact]
    public void AnAliasOfAWinningEntryDropsTheOthersEntryThatUsesIt()
    {
        string merged = ResourceMerge.Merge(
            """[{"name":"set-constant.js","aliases":["set.js"]}]""",
            """[{"name":"set.js"},{"name":"noop.js"}]""");
        Assert.Equal(["set-constant.js", "noop.js"], Names(merged));
    }

    [Fact]
    public void AnEntryWhoseAliasIsTakenIsDroppedWhole()
    {
        string merged = ResourceMerge.Merge(
            """[{"name":"a.js"}]""",
            """[{"name":"c.js","aliases":["a.js"]}]""");
        Assert.Equal(["a.js"], Names(merged));
    }

    [Fact]
    public void ATextThatIsNotAnArrayCountsAsEmpty()
    {
        Assert.Equal("[]", ResourceMerge.Merge("{ no", "{}"));
        Assert.Equal(["x.js"], Names(ResourceMerge.Merge("", """[{"name":"x.js"}]""")));
    }
}
