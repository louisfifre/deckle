using System.Text.Json.Nodes;
using Deckle.Travel;
using Xunit;

namespace Deckle.Travel.Tests;

[Trait("Category", "integration")]
public class TravelUpdateTests
{
    private const string PlaceId = "bafyreiPlaceaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "regression")]
    public async Task UpdateRequiresStableObjectIdsBeforeReadingOrWriting()
    {
        using var space = new FakeTravelSpace();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => space.NewGestures().UpdateAsync(
                [new TravelUpdateItem("Musée", "Musée national", null)], Ct));

        Assert.Contains("id Anytype stable", error.Message);
        Assert.Empty(space.Requests);
    }

    [Fact]
    public async Task UpdateByIdPatchesTheRequestedValues()
    {
        using var space = new FakeTravelSpace();
        space.OnListObjects(Place("Musée"));
        space.OnPatchObject(PlaceId, new JsonObject { ["object"] = Place("Musée national") });

        await space.NewGestures().UpdateAsync(
            [new TravelUpdateItem(PlaceId, "Musée national", null)], Ct);

        JsonObject patch = space.LastBody("PATCH", $"/objects/{PlaceId}");
        Assert.Equal("Musée national", patch["name"]!.GetValue<string>());
    }

    private static JsonObject Place(string name) => new()
    {
        ["id"] = PlaceId,
        ["name"] = name,
        ["type"] = new JsonObject { ["key"] = TravelSchema.Types.Place },
        ["properties"] = new JsonArray(),
    };
}
