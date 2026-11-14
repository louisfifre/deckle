using System.Text.Json.Nodes;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "unit")]
public class SpacePlanFragmentsTests
{
    // The `properties` array a space read through the reader gives.
    private static JsonArray SpaceProperties() => new()
    {
        new JsonObject { ["key"] = "kind", ["name"] = "Kind", ["format"] = "select", ["tags"] = new JsonArray { new JsonObject { ["name"] = "A", ["color"] = "red" } } },
        new JsonObject { ["key"] = "owner", ["name"] = "Owner", ["format"] = "text" },
        new JsonObject { ["key"] = "status", ["name"] = "État", ["format"] = "select" },
    };

    private static JsonObject Args(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void ForType_BuildsOnePlanWithTheSpacesOwnPropertyDeclarations()
    {
        JsonObject plan = SpacePlanFragments.ForType(SpaceProperties(), Args(
            """{"key":"recipe","name":"Recette","properties":["kind","owner"],"panel":{"featured":["kind"],"hidden":[]}}"""));

        Assert.Equal(SpacePlan.CurrentVersion, plan["plan_version"]!.GetValue<int>());
        JsonArray declared = plan["properties"]!.AsArray();
        Assert.Equal(["kind", "owner"], declared.Select(p => p!["key"]!.GetValue<string>()));
        Assert.Equal("red", declared[0]!["tags"]![0]!["color"]!.GetValue<string>());

        JsonObject type = plan["types"]!.AsArray().Single()!.AsObject();
        Assert.Equal("recipe", type["key"]!.GetValue<string>());
        Assert.Equal(["kind", "owner"], type["properties"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Equal(["kind"], type["panel"]!["featured"]!.AsArray().Select(p => p!.GetValue<string>()));
    }

    [Fact]
    public void ForType_CountsPanelKeysAsTheTypesOwnProperties()
    {
        JsonObject plan = SpacePlanFragments.ForType(SpaceProperties(), Args(
            """{"key":"recipe","name":"Recette","properties":["owner"],"panel":{"featured":["kind"]}}"""));

        JsonObject type = plan["types"]!.AsArray().Single()!.AsObject();
        Assert.Equal(["owner", "kind"], type["properties"]!.AsArray().Select(p => p!.GetValue<string>()));
        Assert.Empty(type["panel"]!["hidden"]!.AsArray());
        // The fragment passes the plan validator: nothing in the panel is outside the properties.
        Assert.Empty(SpacePlanValidator.Validate(SpacePlan.Parse(plan)));
    }

    [Fact]
    public void ForType_RefusesAPropertyTheSpaceDoesNotHold()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => SpacePlanFragments.ForType(SpaceProperties(), Args(
            """{"key":"recipe","name":"Recette","properties":["ghost"]}""")));
        Assert.Contains("ghost", error.Message);
        Assert.Contains("kind", error.Message);
    }

    [Fact]
    public void ForType_LetsAPropertyEveryRenamedOrSystemSpaceShipsThrough()
    {
        // `description` ships with every space and a plan never declares it; `status` is declared by the space.
        JsonObject plan = SpacePlanFragments.ForType(SpaceProperties(), Args(
            """{"key":"recipe","name":"Recette","properties":["description","status"]}"""));
        Assert.Equal(["status"], plan["properties"]!.AsArray().Select(p => p!["key"]!.GetValue<string>()));
    }

    [Fact]
    public void ForType_RefusesAMissingNameAndAnUnknownField()
    {
        Assert.Throws<ArgumentException>(() => SpacePlanFragments.ForType(SpaceProperties(), Args("""{"key":"recipe"}""")));
        Assert.Throws<ArgumentException>(() => SpacePlanFragments.ForType(SpaceProperties(), Args(
            """{"key":"recipe","name":"Recette","colour":"red"}""")));
    }

    [Fact]
    public void ForType_DoesNotMutateItsInputs()
    {
        JsonArray space = SpaceProperties();
        JsonObject args = Args("""{"key":"recipe","name":"Recette","properties":["kind"]}""");
        string before = space.ToJsonString() + args.ToJsonString();
        SpacePlanFragments.ForType(space, args);
        Assert.Equal(before, space.ToJsonString() + args.ToJsonString());
    }
}
