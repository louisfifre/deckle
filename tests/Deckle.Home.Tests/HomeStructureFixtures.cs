using System.Text.Json.Nodes;

namespace Deckle.Home.Tests;

// The structure of the synthetic source space of HomeRehearsalFixtures, on the
// OLD keys and old ids: type settings and views for errand, room, device,
// worksite and todo, two views on the section collection, templates (one for
// a type that is not Home's), widgets (one personal, one on an object that
// does not travel). Every name is fictional.
internal static class HomeStructureFixtures
{
    public static JsonObject Obj(string text) => (JsonObject)JsonNode.Parse(text)!;

    public static JsonArray Arr(string text) => (JsonArray)JsonNode.Parse(text)!;

    // Source object ids of HomeRehearsalFixtures that the replay recreates.
    public const string OldTemplateRoom = "old-template-room";
    public const string OldTemplateDevice = "old-template-device";
    public const string OldTemplateZone = "old-template-zone";
    public const string OldTemplatePage = "old-template-page";

    public static FakeHomeAnytypeServer Source()
    {
        FakeHomeAnytypeServer source = HomeRehearsalFixtures.Source();
        source.TypeIdPrefix = "src-type-";
        source.RemoveSchemaType("product");
        source.SeedDefaultTypeStructure();

        source.SetTypeStructure("errand", "kanban", null,
            Arr("""
            [{"property":"errand_category","section":"featured"},{"property":"needed","section":"hidden"},
             {"property":"aisle","section":"hidden"},{"property":"quantity"}]
            """),
            Arr("""
            [{"id":"vE1","name":"All","sorts":[{"property":"last_modified_date","direction":"desc","id":"s1"},{"property":"aisle","direction":"asc","id":"s2"}],
              "filters":[{"property":"aisle","condition":"not_empty","id":"f1"},{"property":"needed","condition":"equal","value":true,"id":"f2"}],
              "columns":[{"property":"name","width":200},{"property":"errand_category","width":120},{"property":"aisle","width":100},{"property":"needed","hidden":true}]},
             {"id":"vE2","type":"kanban","name":"Par catégorie","group_by":"errand_category","card_size":"small",
              "columns":[{"property":"name"},{"property":"errand_category"},{"property":"quantity","aggregation":"sum"},{"property":"lastOpenedDate","hidden":true}]}]
            """));

        source.SetTypeStructure("room", "list", OldTemplateRoom,
            Arr("""
            [{"property":"code","section":"featured"},{"property":"floor","section":"featured"}]
            """),
            Arr("""
            [{"id":"vR1","type":"list","name":"Pièces","group_by":"floor",
              "sorts":[{"property":"floor","direction":"asc"},{"property":"name","direction":"asc"}],
              "filters":[{"property":"floor","condition":"in","value":["floor-1","bafyreigonegonegonegonegonegonegone"]},{"property":"code","condition":"not_empty"}],
              "columns":[{"property":"name"},{"property":"floor","width":150},{"property":"code"}]}]
            """));

        source.SetTypeStructure("device", null, OldTemplateDevice,
            Arr("""
            [{"property":"manufacturer","section":"featured"},{"property":"power_watts","section":"hidden"},{"property":"purchase_date"}]
            """),
            Arr("""
            [{"id":"vD1","name":"All",
              "sorts":[{"property":"power_watts","direction":"desc"},{"property":"purchase_date","direction":"asc"}],
              "columns":[{"property":"name"},{"property":"power_watts"},{"property":"manufacturer"},{"property":"purchase_date","hidden":true}]}]
            """));

        source.SetTypeStructure("worksite", null, null,
            Arr("""[{"property":"state","section":"featured"}]"""),
            Arr("""
            [{"id":"vW1","name":"All","columns":[{"property":"name"},{"property":"state"}]},
             {"id":"vW2","type":"kanban","name":"Par état","group_by":"state","columns":[{"property":"name"}]},
             {"id":"vW3","type":"list","name":"En cours","filters":[{"property":"state","condition":"in","value":["En cours"]}],"columns":[{"property":"name"}]},
             {"id":"vW4","type":"list","name":"Tout","columns":[{"property":"name"},{"property":"estimated_budget","aggregation":"sum"}]}]
            """));

        source.SetTypeStructure("todo", null, null,
            Arr("""[{"property":"worksite","section":"featured"},{"property":"priority","section":"featured"}]"""),
            Arr("""
            [{"id":"vT1","name":"All","sorts":[{"property":"worksite","direction":"asc"}],"columns":[{"property":"name"},{"property":"worksite"},{"property":"done"}]}]
            """));

        // Section collection: the object "section-1" of the object fixtures.
        source.SetCollectionViews("section-1", Arr("""
            [{"id":"cv1","name":"All","columns":[{"property":"name"},{"property":"type"},{"property":"created_date","hidden":true}]},
             {"id":"cv2","type":"list","name":"Par nom","sorts":[{"property":"name","direction":"asc"}],"columns":[{"property":"name"},{"property":"manufacturer"},{"property":"power_watts"}]}]
            """));

        source.AddTemplateDocument(Obj("""
            {"kind":"template","id":"old-template-room","type":"template","template_for":"room",
             "properties":{"name":"Modèle pièce fictif","created_date":"2026-01-01","creator":"someone","layout":"basic","resolved_layout":"basic",
                           "target_object_type":["x"],"source_object":["y"],"tag":["z"],"floor":["floor-1"],"notes":"texte fictif","code":"ZZ"},
             "blocks":[{"id":"b1","type":"heading_2","text":"Relevé fictif"},
                       {"id":"b2","type":"row"},{"id":"b3","type":"column","indent":1,"fields":{"width":0.5}},
                       {"id":"b3b","type":"column","indent":2},
                       {"id":"b4","type":"property","property":"floor","indent":3},
                       {"id":"b5","type":"property","property":"aisle","indent":3},
                       {"id":"b6","type":"divider"},{"id":"b7","type":"paragraph","text":"Texte fictif"},
                       {"id":"b8","type":"dataview","object_id":"type-errand","source":["type-errand"],"fields":{"k":1},
                        "properties":[{"property":"aisle","format":"text"},{"property":"errand_category","format":"select"}],
                        "views":[{"id":"x1","name":"Produits","type":"list","groups":[{"id":"g"}],"object_orders":[],
                                 "sorts":[{"property":"aisle","id":"s9"},{"property":"name","id":"s8"}],
                                 "columns":[{"property":"name"},{"property":"aisle"}],
                                 "filters":[{"property":"aisle","condition":"empty","id":"f9"},{"property":"errand_category","condition":"in","value":["Produits céréaliers secs"],"id":"f8","nested_property":"n"}]}]}]}
            """));

        source.AddTemplateDocument(Obj("""
            {"kind":"template","id":"old-template-device","type":"template","template_for":"device",
             "properties":{"name":"Modèle appareil fictif","domain":"Audio","equipment_category":"Catégorie fictive","purchase_price":12,
                           "power_watts":40,"receipt":["file-1"],"battery_capacity":3000,"manufacturer":"Marque fictive","last_modified_date":"2026-01-02"},
             "blocks":[{"id":"d1","type":"heading_2","text":"Achat"},{"id":"d2","type":"property","property":"purchase_price"},
                       {"id":"d3","type":"table","rows":[{"id":"r1","cells":["a","b"]}],"columns":[{"id":"c1","fields":{"w":1},"header":"a"},{"id":"c2"}]}]}
            """));

        source.AddTemplateDocument(Obj("""
            {"kind":"template","id":"old-template-zone","type":"template","template_for":"zone",
             "properties":{"name":"Modèle zone fictif"},
             "blocks":[{"id":"z1","type":"dataview","object_id":"type-room",
                        "views":[{"id":"zv","name":"Pièces","filters":[{"property":"floor","condition":"in","value":["_filter_template_1_"]}]}]}]}
            """));

        source.AddTemplateDocument(Obj("""
            {"kind":"template","id":"old-template-page","type":"template","template_for":"page",
             "properties":{"name":"Modèle de page fictif"},"blocks":[]}
            """));

        // The section collection holds a room, a device and a page (a page does
        // not travel), and carries an icon. The room comes first because the
        // replay adds the memberships of a collection the target already holds
        // in its own creation order (rooms before devices), and the write adds
        // nothing a collection already holds.
        source.SetCollectionMembers("section-1", "room-1", "device-1", "page-1");
        source.SetObjectIcon("section-1", Obj("""{"format":"emoji","emoji":"X"}"""));

        source.AddWidget("sw1", "space", "src-type-errand", "view", 6, "vE2");
        source.AddWidget("sw2", "space", "section-1", "list", 4, "cv2");
        source.AddWidget("sw3", "space", "room-1", "link");
        source.AddWidget("sw4", "space", "page-1", "link");
        source.AddWidget("sw5", "personal", "room-1", "link");
        source.AddWidget("sw6", "space", "src-type-room", "view", 6, "vR1");
        source.AddWidget("sw7", "space", "section-1", "view", 6, "vGone");
        return source;
    }

    // The target as a fresh space holds it: the manifest types with a default
    // view each, and the section collection.
    public static FakeHomeAnytypeServer Target()
    {
        FakeHomeAnytypeServer target = HomeRehearsalFixtures.Target();
        target.TypeIdPrefix = "tgt-type-";
        target.SeedDefaultTypeStructure();
        return target;
    }
}
