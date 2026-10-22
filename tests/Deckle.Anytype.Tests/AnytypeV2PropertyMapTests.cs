using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The map is read against the shape measured live on 2026-10-22; the JSON below
// is synthetic but keeps that shape (object keyed by property key, select as an
// array of option names, objects as an array of bare ids, checkbox only when true).
[Trait("Category", "unit")]
public class AnytypeV2PropertyMapTests
{
    static AnytypeV2PropertyMap MapOf(string propertiesJson) =>
        new((JsonObject)JsonNode.Parse(propertiesJson)!);

    [Fact]
    public void ASelectIsReadAsItsFirstOptionNameAndAMultiSelectAsAllOfThem()
    {
        AnytypeV2PropertyMap map = MapOf("""{"etat":["Ouvert"],"livrable(s)":["Texte","Règle de cadrage"]}""");

        Assert.Equal("Ouvert", map.Select("etat"));
        Assert.Equal(new[] { "Texte", "Règle de cadrage" }, map.Names("livrable(s)"));
    }

    [Fact]
    public void ObjectsAreBareIdsInServedOrder()
    {
        AnytypeV2PropertyMap map = MapOf("""{"relation_projet":["bafyA","bafyB"]}""");

        Assert.Equal(new[] { "bafyA", "bafyB" }, map.ObjectIds("relation_projet"));
        Assert.Equal(2, map.Count("relation_projet"));
    }

    [Fact]
    public void ADateIsCutToItsDay()
    {
        AnytypeV2PropertyMap map = MapOf("""{"due_date":"2026-10-22T00:00:00Z"}""");

        Assert.Equal("2026-10-22", map.Day("due_date"));
    }

    // The server omits an unticked checkbox: silence is « non ».
    [Fact]
    public void ACheckboxIsFalseWhenAbsentAndTrueWhenTicked()
    {
        AnytypeV2PropertyMap map = MapOf("""{"archive":true}""");

        Assert.True(map.Checkbox("archive"));
        Assert.False(map.Checkbox("done"));
    }

    [Fact]
    public void TextAndNumberAreReadAndAnEmptyTextIsNull()
    {
        AnytypeV2PropertyMap map = MapOf("""{"name":"Une tâche","description":"","charge_estimee":1.5}""");

        Assert.Equal("Une tâche", map.Name);
        Assert.Null(map.Text("description"));
        Assert.Equal("1.5", map.Number("charge_estimee"));
        Assert.Null(map.Number("budget_estime"));
    }

    [Fact]
    public void AMapOverNoPropertiesReadsEveryKeyAsMissing()
    {
        var map = new AnytypeV2PropertyMap(null);

        Assert.Equal("", map.Name);
        Assert.Null(map.Select("etat"));
        Assert.Empty(map.ObjectIds("relation_projet"));
        Assert.False(map.InBin);
    }

    // The served key is is_archived; `archived` is only a search alias, and the
    // space's own « Archivé » checkbox (`archive`) is not the bin.
    [Fact]
    public void TheBinIsReadFromIsArchivedOnly()
    {
        Assert.True(MapOf("""{"is_archived":true}""").InBin);
        Assert.False(MapOf("""{"archived":true}""").InBin);
        Assert.False(MapOf("""{"archive":true}""").InBin);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    [InlineData(" 3 ", 3)]
    public void APriorityNameIsItsLevel(string name, int level) =>
        Assert.Equal(level, DevSpace.Priority.LevelForName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("6")]
    [InlineData("haute")]
    public void AnUnknownPriorityNameHasNoLevel(string? name) =>
        Assert.Null(DevSpace.Priority.LevelForName(name));
}
