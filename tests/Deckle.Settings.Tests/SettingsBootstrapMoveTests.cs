using System.Text.Json.Nodes;
using Xunit;

namespace Deckle.Settings.Tests;

// The move of a nested module setting to another module's root, the way the
// segmenter's thresholdDbfs became the Audio module's voiceLevelDbfs.
[Trait("Category", "unit")]
public sealed class SettingsBootstrapMoveTests
{
    private static readonly string[] SegmenterLevel = ["streaming", "segmenter", "thresholdDbfs"];

    [Fact]
    public void ANestedValueMovesToTheTargetRootAndLeavesTheSource()
    {
        var source = JsonNode.Parse("""{"streaming":{"segmenter":{"thresholdDbfs":-38.0,"marginMs":150}}}""")!.AsObject();
        var target = new JsonObject();

        bool moved = SettingsBootstrap.MoveNestedValue(source, SegmenterLevel, target, "voiceLevelDbfs");

        Assert.True(moved);
        Assert.Equal(-38.0, target["voiceLevelDbfs"]!.GetValue<double>());
        var segmenter = source["streaming"]!["segmenter"]!.AsObject();
        Assert.False(segmenter.ContainsKey("thresholdDbfs"));
        Assert.Equal(150, segmenter["marginMs"]!.GetValue<int>());
    }

    [Fact]
    public void AnExistingTargetValueWinsAndTheSourceLeafIsStillRemoved()
    {
        var source = JsonNode.Parse("""{"streaming":{"segmenter":{"thresholdDbfs":-38.0}}}""")!.AsObject();
        var target = JsonNode.Parse("""{"voiceLevelDbfs":-50.0}""")!.AsObject();

        bool moved = SettingsBootstrap.MoveNestedValue(source, SegmenterLevel, target, "voiceLevelDbfs");

        Assert.True(moved);
        Assert.Equal(-50.0, target["voiceLevelDbfs"]!.GetValue<double>());
        Assert.False(source["streaming"]!["segmenter"]!.AsObject().ContainsKey("thresholdDbfs"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"streaming":{}}""")]
    [InlineData("""{"streaming":{"segmenter":{}}}""")]
    [InlineData("""{"streaming":{"segmenter":-45.0}}""")]
    public void AMissingPathTouchesNothing(string sourceJson)
    {
        var source = JsonNode.Parse(sourceJson)!.AsObject();
        var target = new JsonObject();

        bool moved = SettingsBootstrap.MoveNestedValue(source, SegmenterLevel, target, "voiceLevelDbfs");

        Assert.False(moved);
        Assert.Empty(target);
        Assert.Equal(JsonNode.Parse(sourceJson)!.ToJsonString(), source.ToJsonString());
    }
}
