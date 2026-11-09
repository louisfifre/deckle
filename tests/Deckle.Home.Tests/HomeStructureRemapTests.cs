using System.Text.Json.Nodes;
using Deckle.Home;
using Xunit;
using static Deckle.Home.Tests.HomeStructureFixtures;

namespace Deckle.Home.Tests;

// The remap is pure: the source structure and an old → new id map in, the
// structure on the new keys and ids out, with every drop counted by kind.
[Trait("Category", "unit")]
public sealed class HomeStructureRemapTests
{
    const string UnmappedId = "bafyreigonegonegonegonegonegonegone";

    static Dictionary<string, string> Ids() => new(StringComparer.Ordinal)
    {
        ["floor-1"] = "new-zone-1",
        ["old-template-room"] = "new-template-room",
        ["src-type-errand"] = "tgt-type-product",
    };

    [Theory]
    [InlineData("errand_category", null, "product_category")]
    [InlineData("floor", null, "zone")]
    [InlineData("documents", null, "manual")]
    [InlineData("memory_size_gb", null, "memory_size")]
    [InlineData("memory_frequency_mhz", null, "memory_frequency")]
    [InlineData("switch_kind", null, "switch_nature")]
    [InlineData("purchase_date", "device", "purchase_date")]
    [InlineData("name", "device", "name")]
    [InlineData("created_date", "room", "created_date")]
    [InlineData("done", "todo", "done")]
    [InlineData("notes", null, null)]
    [InlineData("power_watts", null, null)]
    [InlineData("condition", null, null)]
    [InlineData("aisle", null, null)]
    [InlineData("a_property_nobody_knows", null, null)]
    [InlineData("manufacturer", "room", null)]
    [InlineData("manufacturer", "device", "manufacturer")]
    public void APropertyKeyGoesThroughTheBridgeOrIsDropped(string source, string? type, string? expected) =>
        Assert.Equal(expected, HomeStructureRemap.PropertyKey(source, type));

    [Theory]
    [InlineData("errand", "product")]
    [InlineData("floor", "zone")]
    [InlineData("room", "room")]
    public void ATypeKeyGoesThroughTheBridge(string source, string expected) =>
        Assert.Equal(expected, HomeStructureRemap.TypeKey(source));

    [Fact]
    public void AViewKeepsWhatTheModelHoldsAndCountsTheRest()
    {
        var remap = new HomeStructureRemap(Ids());
        JsonObject view = Obj("""
            {"id":"vE1","name":"All","type":"list","group_by":"aisle","card_size":"small",
             "sorts":[{"property":"last_modified_date","direction":"desc","id":"s1"},{"property":"aisle","direction":"asc","id":"s2"}],
             "filters":[{"property":"aisle","condition":"not_empty","id":"f1"},{"property":"needed","condition":"equal","value":true,"id":"f2"}],
             "columns":[{"property":"name","width":200},{"property":"errand_category","width":120},{"property":"aisle"},{"property":"needed","hidden":true}]}
            """);

        HomeViewPlan plan = remap.ViewPlan(view, "product", "type product");

        Assert.Equal("All", plan.Name);
        Assert.Equal("list", plan.Set["type"]!.GetValue<string>());
        Assert.Equal("small", plan.Set["card_size"]!.GetValue<string>());
        Assert.Null(plan.Set["group_by"]);
        Assert.Equal(["last_modified_date"], plan.Set["sorts"]!.AsArray().Select(sort => sort!["property"]!.GetValue<string>()));
        Assert.Null(plan.Set["sorts"]![0]!["id"]);
        JsonObject filter = Assert.IsType<JsonObject>(Assert.Single(plan.Set["filters"]!.AsArray()));
        Assert.Equal("needed", filter["property"]!.GetValue<string>());
        Assert.Equal(["name", "product_category"], plan.Visible);
        Assert.Equal(120, plan.Columns["product_category"]!["width"]!.GetValue<int>());
        Assert.False(plan.Columns["product_category"]!["hidden"]!.GetValue<bool>());
        Assert.True(plan.Columns["needed"]!["hidden"]!.GetValue<bool>());
        Assert.Null(plan.Columns["aisle"]);
        Assert.Equal(1, remap.Dropped["column"]);
        Assert.Equal(1, remap.Dropped["sort"]);
        Assert.Equal(1, remap.Dropped["filter"]);
        Assert.Equal(1, remap.Dropped["group_by"]);
    }

    [Fact]
    public void AViewWithNoSortsOrFiltersSendsEmptyListsSoTheTargetsDefaultsDoNotStay()
    {
        var remap = new HomeStructureRemap(Ids());

        HomeViewPlan plan = remap.ViewPlan(Obj("""{"id":"v","name":"All","columns":[{"property":"name"}]}"""), "room", "x");

        Assert.Empty(plan.Set["sorts"]!.AsArray());
        Assert.Empty(plan.Set["filters"]!.AsArray());
        Assert.Empty(remap.Dropped);
    }

    [Fact]
    public void AFilterValueNamesTheNewObjectAndAnUnmappedIdIsDroppedWithItsMember()
    {
        var remap = new HomeStructureRemap(Ids());
        JsonObject view = Obj($$"""
            {"name":"v","filters":[
              {"property":"floor","condition":"in","value":["floor-1","{{UnmappedId}}"]},
              {"property":"floor","condition":"in","value":["{{UnmappedId}}"]},
              {"property":"floor","condition":"in","value":["_filter_template_1_"]},
              {"property":"code","condition":"equal","value":"ZZ"}]}
            """);

        HomeViewPlan plan = remap.ViewPlan(view, "room", "type room");

        JsonArray filters = plan.Set["filters"]!.AsArray();
        Assert.Equal(3, filters.Count);
        Assert.Equal(["new-zone-1"], filters[0]!["value"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("zone", filters[0]!["property"]!.GetValue<string>());
        Assert.Equal(HomeStructureRemap.ThisObjectToken, filters[1]!["value"]![0]!.GetValue<string>());
        Assert.Equal("ZZ", filters[2]!["value"]!.GetValue<string>());
        Assert.Equal(2, remap.Dropped["id"]);
        Assert.Equal(1, remap.Dropped["filter"]);
    }

    [Fact]
    public void ASelectFilterReadsItsOptionsThroughTheManifest()
    {
        var remap = new HomeStructureRemap(Ids());
        JsonObject view = Obj("""
            {"name":"v","filters":[
              {"property":"state","condition":"in","value":["En cours","open","Un état inconnu"]},
              {"property":"state","condition":"in","value":["Un état inconnu"]},
              {"property":"manufacturer","condition":"in","value":["Marque fictive"]}]}
            """);

        HomeViewPlan plan = remap.ViewPlan(view, "worksite", "type worksite");

        JsonArray filters = plan.Set["filters"]!.AsArray();
        Assert.Equal(["In progress", "Open"], filters[0]!["value"]!.AsArray().Select(value => value!.GetValue<string>()));
        // manufacturer is not on a worksite: its filter goes with the property.
        Assert.Single(filters);
        Assert.Equal(2, remap.Dropped["filter"]);
        Assert.Equal(2, remap.Dropped["option"]);
    }

    [Fact]
    public void AGroupFilterKeepsItsSurvivorsAndGoesWhenNoneSurvives()
    {
        var remap = new HomeStructureRemap(Ids());
        JsonObject view = Obj("""
            {"name":"v","filters":[
              {"operator":"or","filters":[{"property":"aisle","condition":"empty"},{"property":"needed","condition":"equal","value":false}]},
              {"operator":"and","filters":[{"property":"aisle","condition":"empty"}]}]}
            """);

        HomeViewPlan plan = remap.ViewPlan(view, "product", "type product");

        JsonObject group = Assert.IsType<JsonObject>(Assert.Single(plan.Set["filters"]!.AsArray()));
        Assert.Equal("or", group["operator"]!.GetValue<string>());
        Assert.Single(group["filters"]!.AsArray());
    }

    [Fact]
    public void ATypePlanBridgesTheKeyAndKeepsOnlyTheSectionsOfPropertiesThatExist()
    {
        var remap = new HomeStructureRemap(Ids());
        var type = new HomeStructureType
        {
            Key = "errand",
            TypeId = "src-type-errand",
            DefaultView = "kanban",
            DefaultTemplate = "old-template-room",
            Properties =
            [
                new() { Property = "errand_category", Section = "featured" },
                new() { Property = "aisle", Section = "hidden" },
                new() { Property = "quantity" },
                new() { Property = "power_watts" },
            ],
            Views = Arr("""[{"id":"a","name":"All","columns":[{"property":"name"}]}]"""),
        };

        HomeTypePlan plan = remap.TypePlan(type);

        Assert.Equal("product", plan.Key);
        Assert.Equal("errand", plan.OldKey);
        Assert.Equal("kanban", plan.DefaultView);
        Assert.Equal(
            [("product_category", "featured"), ("quantity", null)],
            plan.Properties.Select(property => (property.Property, property.Section)));
        Assert.Equal(1, remap.Dropped["section"]);
        Assert.Single(plan.Views);
    }

    [Fact]
    public void ATemplateKeepsTheManifestPropertiesOfItsTypeAndMapsRelationsAndOptions()
    {
        var remap = new HomeStructureRemap(Ids());
        HomeStructureTemplate template = new()
        {
            OldId = "old-template-device",
            Name = "Modèle appareil fictif",
            TemplateFor = "device",
            Document = Obj($$"""
                {"kind":"template","id":"x","template_for":"device",
                 "properties":{"name":"Modèle appareil fictif","created_date":"d","creator":"c","layout":"basic","resolved_layout":"basic",
                               "target_object_type":["t"],"source_object":["s"],"tag":["g"],"last_modified_by":"m","last_modified_date":"d",
                               "domain":"Audio","equipment_category":"Catégorie fictive","purchase_price":12,"power_watts":40,"receipt":["file-1"],
                               "installed_in":["floor-1","{{UnmappedId}}"],"manufacturer":"Marque fictive","notes":"n"},
                 "blocks":[]}
                """),
        };

        HomeTemplatePlan? plan = remap.TemplatePlan(template);

        Assert.NotNull(plan);
        Assert.Equal("device", plan.Key);
        JsonObject properties = plan.Body["properties"]!.AsObject();
        Assert.Equal(
            ["name", "domain", "equipment_category", "purchase_price", "installed_in", "manufacturer"],
            properties.Select(pair => pair.Key));
        Assert.Equal("Modèle appareil fictif", properties["name"]!.GetValue<string>());
        Assert.Equal(["new-zone-1"], properties["installed_in"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Equal("template", plan.Body["kind"]!.GetValue<string>());
        Assert.Equal("device", plan.Body["template_for"]!.GetValue<string>());
        // power_watts, receipt (a file), notes: not Home's any more. The app's
        // bookkeeping goes without a count.
        Assert.Equal(3, remap.Dropped["template_property"]);
        Assert.Equal(1, remap.Dropped["id"]);
    }

    [Fact]
    public void ATemplateBodyLosesItsOutputOnlyMembersAndItsDroppedPropertiesAndKeepsItsShape()
    {
        var remap = new HomeStructureRemap(Ids());
        HomeStructureTemplate template = new()
        {
            OldId = "old-template-room",
            Name = "Modèle pièce fictif",
            TemplateFor = "room",
            Document = Obj("""
                {"kind":"template","template_for":"room","properties":{"name":"Modèle pièce fictif"},
                 "blocks":[{"id":"b1","type":"heading_2","text":"Relevé fictif"},
                           {"id":"b2","type":"row"},{"id":"b3","type":"column","indent":1,"fields":{"width":0.5}},
                           {"id":"b4","type":"property","property":"floor","indent":2},
                           {"id":"b5","type":"property","property":"aisle","indent":2},
                           {"id":"b6","type":"divider"},
                           {"id":"b7","type":"table","rows":[{"id":"r1","cells":["a",{"id":"c","type":"paragraph","text":"b","fields":{"k":1}}]}],"columns":[{"id":"c1","fields":{"w":1},"header":"a"}]},
                           {"id":"b8","type":"image","object_id":"file-1"},
                           {"id":"b9","type":"dataview","object_id":"type-errand","source":["type-errand"],"fields":{"k":1},
                            "properties":[{"property":"aisle","format":"text"},{"property":"errand_category","format":"select"}],
                            "views":[{"id":"x1","name":"Produits","type":"list","groups":[{"id":"g"}],"object_orders":[],
                                     "sorts":[{"property":"aisle","id":"s9"},{"property":"name","id":"s8"}],
                                     "columns":[{"property":"name"},{"property":"aisle"}],
                                     "filters":[{"property":"aisle","condition":"empty","id":"f9"},{"property":"errand_category","condition":"in","value":["Produits céréaliers secs"],"id":"f8","nested_property":"n"}]}]},
                           {"id":"b10","type":"dataview","object_id":"bafyreinotthereinotthereinotthere"},
                           {"id":"b11","type":"dataview","object_id":"floor-1","views":[{"name":"Pièces","filters":[{"property":"floor","condition":"in","value":["_filter_template_1_"]}]}]}]}
                """),
        };

        HomeTemplatePlan? plan = remap.TemplatePlan(template);

        JsonArray blocks = plan!.Body["blocks"]!.AsArray();
        Assert.Equal(
            ["heading_2", "row", "column", "property", "divider", "table", "dataview", "dataview"],
            blocks.Select(block => block!["type"]!.GetValue<string>()));
        Assert.All(blocks, block => Assert.Null(block!["id"]));
        Assert.All(blocks, block => Assert.Null(block!["fields"]));
        Assert.All(blocks, block => Assert.Null(block!["source"]));
        Assert.Equal("zone", blocks[3]!["property"]!.GetValue<string>());

        JsonObject table = blocks[5]!.AsObject();
        Assert.Null(table["columns"]![0]!["fields"]);
        Assert.Null(table["columns"]![0]!["header"]);
        JsonObject cell = table["rows"]![0]!["cells"]![1]!.AsObject();
        Assert.Null(cell["id"]);
        Assert.Null(cell["fields"]);

        JsonObject dataview = blocks[6]!.AsObject();
        Assert.Equal("type-product", dataview["object_id"]!.GetValue<string>());
        Assert.Equal(["product_category"], dataview["properties"]!.AsArray().Select(entry => entry!["property"]!.GetValue<string>()));
        JsonObject view = dataview["views"]![0]!.AsObject();
        Assert.Null(view["id"]);
        Assert.Null(view["groups"]);
        Assert.Null(view["object_orders"]);
        Assert.Equal(["name"], view["sorts"]!.AsArray().Select(sort => sort!["property"]!.GetValue<string>()));
        Assert.Equal(["name"], view["columns"]!.AsArray().Select(column => column!["property"]!.GetValue<string>()));
        JsonObject kept = Assert.IsType<JsonObject>(Assert.Single(view["filters"]!.AsArray()));
        Assert.Equal("product_category", kept["property"]!.GetValue<string>());
        Assert.Equal(["Dry cereal products"], kept["value"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Null(kept["id"]);
        Assert.Null(kept["nested_property"]);

        // The inline query on a recreated object follows it; « this object »
        // passes untouched.
        JsonObject onObject = blocks[7]!.AsObject();
        Assert.Equal("new-zone-1", onObject["object_id"]!.GetValue<string>());
        Assert.Equal(
            HomeStructureRemap.ThisObjectToken,
            onObject["views"]![0]!["filters"]![0]!["value"]![0]!.GetValue<string>());

        Assert.Equal(1, remap.Dropped["property_block"]);
        Assert.Equal(2, remap.Dropped["block"]);
        Assert.Equal(1, remap.Dropped["dataview_property"]);
    }

    [Fact]
    public void ATemplateOfATypeTheModelDoesNotHoldIsDropped()
    {
        var remap = new HomeStructureRemap(Ids());

        HomeTemplatePlan? plan = remap.TemplatePlan(new HomeStructureTemplate
        {
            OldId = "t", Name = "Modèle", TemplateFor = "page", Document = Obj("""{"properties":{"name":"Modèle"},"blocks":[]}"""),
        });

        Assert.Null(plan);
        Assert.Equal(1, remap.Dropped["template"]);
    }

    [Fact]
    public void AWidgetFollowsItsTargetAndIsDroppedWhenTheTargetDoesNotTravel()
    {
        var remap = new HomeStructureRemap(Ids());

        HomeWidgetPlan? onType = remap.WidgetPlan(new HomeStructureWidget
        {
            OldId = "w1", Target = "src-type-errand", Layout = "view", Limit = 6, ViewId = "vE2",
        });
        HomeWidgetPlan? lost = remap.WidgetPlan(new HomeStructureWidget { OldId = "w2", Target = "page-1", Layout = "link" });

        Assert.Equal(("tgt-type-product", "view", 6, "vE2"), (onType!.Target, onType.Layout, onType.Limit, onType.OldViewId));
        Assert.Null(lost);
        Assert.Equal(1, remap.Dropped["widget"]);
    }

    [Fact]
    public void TheDetailsNameWhatWasDroppedAndTheCountsDoNot()
    {
        var remap = new HomeStructureRemap(Ids());

        remap.ViewPlan(Obj("""{"name":"Vue fictive","columns":[{"property":"aisle"}]}"""), "product", "type product");

        HomeStructureDrop drop = Assert.Single(remap.Details);
        Assert.Equal(("column", "aisle"), (drop.Kind, drop.What));
        Assert.Contains("Vue fictive", drop.Where);
        Assert.DoesNotContain("Vue fictive", string.Join(",", remap.Dropped.Keys));
    }
}
