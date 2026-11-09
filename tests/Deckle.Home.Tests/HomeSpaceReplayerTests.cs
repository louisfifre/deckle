using Deckle.Home;
using Xunit;

namespace Deckle.Home.Tests;

// The pure pieces of the replay: the order, the ids read from a create's
// digest, and the name-free reason a refusal is counted under.
[Trait("Category", "unit")]
public sealed class HomeSpaceReplayerTests
{
    [Fact]
    public void TheReplayFollowsTheOrderOfTheDesignNote() =>
        Assert.Equal(
            ["zone", "room", "panel", "circuit", "point", "system", "device", "component", "utensil",
             "plant", "product", "idea", "worksite", "todo"],
            HomeSpaceReplayer.Order);

    [Fact]
    public void EveryCreatableTypeHasAPlaceInTheOrder() =>
        Assert.Equal(
            HomeSchema.CreatableTypes.OrderBy(type => type, StringComparer.Ordinal),
            HomeSpaceReplayer.Order.OrderBy(type => type, StringComparer.Ordinal));

    [Fact]
    public void TheCreatedIdsAreReadInTheOrderOfTheItems()
    {
        IReadOnlyList<string?> ids = HomeSpaceReplayer.CreatedIds(
            "Créé :\n- device · Appareil fictif (id obj-1)\n- device · Appareil (id) fictif\n- device · Autre fictif (id obj-3)");

        Assert.Equal(["obj-1", null, "obj-3"], ids);
    }

    [Fact]
    public void PanelsAreCreatedRootsFirst()
    {
        var leaf = new HomeReplayObject { OldId = "leaf", Type = "panel", References = new() { ["upstream_panel"] = ["middle"] } };
        var middle = new HomeReplayObject { OldId = "middle", Type = "panel", References = new() { ["upstream_panel"] = ["root"] } };
        var root = new HomeReplayObject { OldId = "root", Type = "panel" };

        HomeReplayObject[] ordered = HomeSpaceReplayer.OrderWithinType("panel", [leaf, middle, root]);

        Assert.Equal(["root", "middle", "leaf"], ordered.Select(panel => panel.OldId));
    }

    [Fact]
    public void AnUpstreamLoopDoesNotHangTheOrder()
    {
        var a = new HomeReplayObject { OldId = "a", Type = "panel", References = new() { ["upstream_panel"] = ["b"] } };
        var b = new HomeReplayObject { OldId = "b", Type = "panel", References = new() { ["upstream_panel"] = ["a"] } };

        Assert.Equal(2, HomeSpaceReplayer.OrderWithinType("panel", [a, b]).Length);
    }

    [Theory]
    [InlineData("Option inconnue ou ambiguë « X » pour Fabricant. Options présentes : Y", "option absente de la cible")]
    [InlineData("Catégorie inconnue « SA ». Catégories admises : PS.", "catégorie hors des 22")]
    [InlineData("Le code « ZZ-PS01 » existe déjà. Prochain code libre proposé : ZZ-PS02.", "code déjà pris")]
    [InlineData("Un composant n'existe que dans son système : fournis « Fait partie de ».", "composant sans système")]
    [InlineData("Aucun objet Home trouvé pour « X ».", "relation introuvable")]
    [InlineData("Quelque chose d'imprévu.", "autre refus")]
    public void ARefusalIsCountedUnderANameFreeReason(string message, string reason) =>
        Assert.Equal(reason, HomeSpaceReplayer.ReasonOf(new InvalidOperationException(message)));

    [Fact]
    public void OnlyAValidationIsARefusal()
    {
        Assert.True(HomeSpaceReplayer.IsRefusal(new ArgumentException("refus")));
        Assert.True(HomeSpaceReplayer.IsRefusal(new InvalidOperationException("refus")));
        Assert.False(HomeSpaceReplayer.IsRefusal(new HomeSchemaException("schéma")));
        Assert.False(HomeSpaceReplayer.IsRefusal(new System.Net.Http.HttpRequestException("transport")));
    }
}
