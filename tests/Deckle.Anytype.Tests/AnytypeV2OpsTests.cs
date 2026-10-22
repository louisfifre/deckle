using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public class AnytypeV2OpsTests
{
    // The ops GET /v2/schemas serves, minus the four view ops (update_view,
    // insert_view, move_view, delete_view), left unmodeled because no gesture
    // edits a view. The wire names are the promise here.
    private static readonly string[] ServedKinds =
    [
        "set_properties", "set_type", "update_block", "replace_subtree", "insert_blocks",
        "move_block", "delete_block", "replace_text", "set_cell", "add_items", "remove_items",
        "add_property", "remove_property", "move_property",
    ];

    private static IEnumerable<AnytypeV2Op> OneOfEach()
    {
        JsonObject block = AnytypeV2Block.Create("paragraph", "text");
        yield return AnytypeV2ObjectOps.SetProperties(new AnytypeV2PropertyChanges().Set("done", true));
        yield return AnytypeV2ObjectOps.SetType("task");
        yield return AnytypeV2BlockOps.UpdateBlock(AnytypeV2BlockRef.ById("b1"), new JsonObject { ["text"] = "x" });
        yield return AnytypeV2BlockOps.ReplaceSubtree("b1", [block]);
        yield return AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.End, [block]);
        yield return AnytypeV2BlockOps.MoveBlock("b1", AnytypeV2Placement.After("b2"));
        yield return AnytypeV2BlockOps.DeleteBlock(AnytypeV2BlockRef.ById("b1"));
        yield return AnytypeV2BlockOps.ReplaceText("a", "b");
        yield return AnytypeV2BlockOps.SetCell("t1", "r1", "c1", JsonValue.Create("v"));
        yield return AnytypeV2ObjectOps.AddItems(["o1"]);
        yield return AnytypeV2ObjectOps.RemoveItems(["o1"]);
        yield return AnytypeV2TypeOps.AddProperty("Season", AnytypeV2PropertyFormat.Select);
        yield return AnytypeV2TypeOps.RemoveProperty("Season");
        yield return AnytypeV2TypeOps.MoveProperty("Season", AnytypeV2Placement.Start);
    }

    [Fact]
    public void EveryServedOpHasAFactoryAndItsEntryNamesItself()
    {
        List<AnytypeV2Op> ops = OneOfEach().ToList();

        Assert.Equal(ServedKinds.Order(), ops.Select(op => op.Kind).Order());
        Assert.All(ops, op => Assert.Equal(op.Kind, op.Json["op"]!.GetValue<string>()));
    }

    [Fact]
    public void ABlockIsNamedByIdOrByTextNeverBoth()
    {
        JsonObject byId = AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById("b1"), true).Json;
        JsonObject byText = AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ByText("Draft timeline"), true).Json;

        Assert.Equal("b1", byId["id"]!.GetValue<string>());
        Assert.False(byId.ContainsKey("match"));
        Assert.Equal("Draft timeline", byText["match"]!.GetValue<string>());
        Assert.False(byText.ContainsKey("id"));
    }

    [Fact]
    public void TickingACheckboxIsAnUpdateBlockThatSetsChecked()
    {
        AnytypeV2Op op = AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById("b1"), isChecked: true);

        Assert.Equal(
            """{"op":"update_block","id":"b1","set":{"checked":true}}""",
            op.Json.ToJsonString());
    }

    [Fact]
    public void AnEmptyUpdateBlockIsRefusedBeforeItCostsARequest()
    {
        Assert.Throws<ArgumentException>(() =>
            AnytypeV2BlockOps.UpdateBlock(AnytypeV2BlockRef.ById("b1"), new JsonObject()));
    }

    [Fact]
    public void APlacementWritesAtMostOneTargetAndOnlyAllowedPositions()
    {
        static string Placed(AnytypeV2Placement placement) =>
            AnytypeV2BlockOps.InsertMarkdown(placement, "- [ ] todo").Json.ToJsonString();

        Assert.Equal("""{"op":"insert_blocks","after":"b3","markdown":"- [ ] todo"}""", Placed(AnytypeV2Placement.After("b3")));
        Assert.Equal("""{"op":"insert_blocks","inside":"b3","markdown":"- [ ] todo"}""", Placed(AnytypeV2Placement.Inside("b3")));
        Assert.Equal("""{"op":"insert_blocks","inside":"b3","position":"first","markdown":"- [ ] todo"}""", Placed(AnytypeV2Placement.Inside("b3", atStart: true)));
        Assert.Equal("""{"op":"insert_blocks","position":"first","markdown":"- [ ] todo"}""", Placed(AnytypeV2Placement.Start));
    }

    [Fact]
    public void AMoveNeedsANamedBlockToMoveRelativeTo()
    {
        Assert.Throws<ArgumentException>(() => AnytypeV2BlockOps.MoveBlock("b1", AnytypeV2Placement.End));
    }

    [Fact]
    public void APropertyHasNoContainerToGoInside()
    {
        Assert.Throws<ArgumentException>(() => AnytypeV2TypeOps.MoveProperty("p", AnytypeV2Placement.Inside("b1")));
    }

    [Fact]
    public void ABlockPayloadIsReusableAcrossOpsBecauseItIsCloned()
    {
        JsonObject block = AnytypeV2Block.Create("paragraph", "same");

        AnytypeV2Op first = AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.End, [block]);
        AnytypeV2Op second = AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.Start, [block]);

        Assert.Equal(first.Json["blocks"]!.ToJsonString(), second.Json["blocks"]!.ToJsonString());
    }

    [Fact]
    public void ARelativeIndentOfZeroStaysOffTheWire()
    {
        Assert.False(AnytypeV2Block.Create("paragraph", "x").ContainsKey("relative_indent"));
        Assert.Equal(1, AnytypeV2Block.Create("paragraph", "x", relativeIndent: 1)["relative_indent"]!.GetValue<int>());
    }

    [Fact]
    public void ABlockRunHoldsOneToTwoHundredFiftySixBlocks()
    {
        JsonObject block = AnytypeV2Block.Create("paragraph", "x");

        Assert.Throws<ArgumentException>(() => AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.End, []));
        Assert.Throws<ArgumentException>(() =>
            AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.End, Enumerable.Repeat(block, 257)));
        AnytypeV2BlockOps.InsertBlocks(AnytypeV2Placement.End, Enumerable.Repeat(block, 256));
    }

    [Fact]
    public void ASelectOptionIsSetByNameAndAPropertyKeyLivesInOneVerbOnly()
    {
        var changes = new AnytypeV2PropertyChanges()
            .SetList("status", ["Done"])
            .Add("tag", ["Urgent"])
            .Remove("old_tag", ["Stale"])
            .Unset("reviewer");

        Assert.Equal(
            """{"op":"set_properties","set":{"status":["Done"]},"add":{"tag":["Urgent"]},"remove":{"old_tag":["Stale"]},"unset":["reviewer"]}""",
            AnytypeV2ObjectOps.SetProperties(changes).Json.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => changes.Set("status", "Open"));
        Assert.Throws<InvalidOperationException>(() => changes.Unset("tag"));
    }

    [Fact]
    public void ASetPropertiesWithNoChangeIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AnytypeV2ObjectOps.SetProperties(new AnytypeV2PropertyChanges()));
    }

    [Fact]
    public void APlainSectionIsNullOnTheWireAndFeaturedIsNamed()
    {
        JsonObject plain = AnytypeV2TypeOps.AddProperty("p", section: AnytypeV2PropertySection.Plain).Json;
        JsonObject featured = AnytypeV2TypeOps.AddProperty("p", section: AnytypeV2PropertySection.Featured).Json;
        JsonObject untouched = AnytypeV2TypeOps.AddProperty("p").Json;

        Assert.True(plain.ContainsKey("section"));
        Assert.Null(plain["section"]);
        Assert.Equal("featured", featured["section"]!.GetValue<string>());
        Assert.False(untouched.ContainsKey("section"));
    }

    [Fact]
    public void AnAddPropertyFormatIsSerializedInTheSchemasSnakeCase()
    {
        JsonObject op = AnytypeV2TypeOps.AddProperty("p", AnytypeV2PropertyFormat.MultiSelect).Json;

        Assert.Equal("multi_select", op["format"]!.GetValue<string>());
    }

    [Fact]
    public void ABatchBecomesTheOpsEnvelope()
    {
        JsonObject body = AnytypeV2OpBatch.ToBody(
            [AnytypeV2ObjectOps.SetType("task"), AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById("b1"), true)],
            AnytypeV2OpScope.Object);

        Assert.Equal(2, body["ops"]!.AsArray().Count);
        Assert.Equal("set_type", body["ops"]![0]!["op"]!.GetValue<string>());
    }

    [Fact]
    public void ABatchHoldsOneToFiveHundredTwelveOps()
    {
        AnytypeV2Op op = AnytypeV2ObjectOps.SetType("task");

        Assert.Throws<ArgumentException>(() => AnytypeV2OpBatch.ToBody([], AnytypeV2OpScope.Object));
        Assert.Throws<ArgumentException>(() =>
            AnytypeV2OpBatch.ToBody(Enumerable.Repeat(op, 513).ToList(), AnytypeV2OpScope.Object));
        AnytypeV2OpBatch.ToBody(Enumerable.Repeat(op, 512).ToList(), AnytypeV2OpScope.Object);
    }

    [Fact]
    public void AnOpIsRefusedOnAnEndpointThatDoesNotTakeIt()
    {
        AnytypeV2Op blockOp = AnytypeV2BlockOps.DeleteBlock(AnytypeV2BlockRef.ById("b1"));
        AnytypeV2Op typeOp = AnytypeV2TypeOps.RemoveProperty("p");

        ArgumentException wrongEndpoint = Assert.Throws<ArgumentException>(() =>
            AnytypeV2OpBatch.ToBody([blockOp], AnytypeV2OpScope.Type));
        Assert.Contains("ops[0]", wrongEndpoint.Message);
        Assert.Throws<ArgumentException>(() => AnytypeV2OpBatch.ToBody([typeOp], AnytypeV2OpScope.Object));
    }
}
