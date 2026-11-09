using System.Text.Json.Nodes;

namespace Deckle.Home.Tests;

// A synthetic source space on the OLD keys, as the live Home space was before
// the 9 November model: an errand type with its aisle and category, a floor
// collection holding a room, notes on a point, a point whose category left
// the 22, a device with power_watts, a component in a system, a done task on
// a worksite. Every name is fictional.
internal static class HomeRehearsalFixtures
{
    public const string SourceSpace = "source-space";
    public const string TargetSpace = "target-space";
    public const string Section = "Section fictive";

    public static FakeHomeAnytypeServer Source()
    {
        var server = new FakeHomeAnytypeServer(SourceSpace);
        server.SetObjects(
            Collection("floor-1", "Étage fictif", "floor"),
            Item("room-1", "room", "Pièce fictive", Text("code", "ZZ")),
            Item("point-cs", "point", "Interrupteur fictif",
                Text("code", "ZZ-CS01"),
                Objects("installed_in", "room-1"),
                Select("category", "hash-cs", "CS — interrupteur"),
                Objects("controls", "point-lc"),
                Text("notes", "Note fictive du point")),
            WithBody(Item("point-lc", "point", "Plafonnier fictif",
                Text("code", "ZZ-LC01"),
                Objects("installed_in", "room-1"),
                Select("category", "hash-lc", "LC — plafonnier"),
                Objects("controlled_by", "point-cs"),
                Text("notes", "Note fictive du plafonnier")), "Corps fictif du plafonnier"),
            Item("point-sa", "point", "Point fictif retiré",
                Text("code", "ZZ-SA01"),
                Objects("installed_in", "room-1"),
                Select("category", "hash-sa", "SA — fictive")),
            Item("system-1", "system", "Système fictif"),
            Item("component-1", "component", "Carte fictive",
                Objects("part_of", "system-1"),
                Number("memory_size_gb", 16)),
            Item("device-1", "device", "Appareil fictif",
                Number("power_watts", 40),
                Number("rated_input_power", 35),
                Select("manufacturer", "hash-brand", "Marque fictive"),
                Objects("installed_in", "room-1"),
                Objects("links", "room-1")),
            Item("errand-1", "errand", "Riz fictif",
                Checkbox("needed", true),
                Select("errand_category", "hash-dry", "Produits céréaliers secs"),
                Text("aisle", "Rayon fictif")),
            Item("worksite-1", "worksite", "Chantier fictif",
                Select("state", "hash-progress", "En cours"),
                Number("estimated_budget", 1000)),
            Item("todo-1", "todo", "Tâche fictive",
                Objects("worksite", "worksite-1"),
                Checkbox("done", true)),
            WithBody(Item("idea-1", "idea", ""), "Idée fictive\ndétail fictif"),
            Item("page-1", "page", "Page fictive"),
            Collection("section-1", Section, "collection"));
        server.SetCollectionMembers("floor-1", "room-1");
        server.SetCollectionMembers("section-1", "device-1");
        return server;
    }

    // The target on the compiled manifest, with the section collection the
    // schema application would have made and the open option the source grew.
    public static FakeHomeAnytypeServer Target()
    {
        var server = new FakeHomeAnytypeServer(TargetSpace);
        server.SetObjects(FakeHomeAnytypeServer.Collection("target-section", Section));
        server.AddSchemaTag(HomeSchema.Properties.Manufacturer, "fictional_brand", "Marque fictive");
        return server;
    }

    public static JsonObject Item(string id, string type, string name, params JsonObject[] properties) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = new JsonObject { ["key"] = type },
        ["properties"] = new JsonArray(properties),
    };

    public static JsonObject Collection(string id, string name, string type) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["layout"] = "collection",
        ["type"] = new JsonObject { ["key"] = type, ["layout"] = "collection" },
        ["properties"] = new JsonArray(),
    };

    public static JsonObject WithBody(JsonObject value, string markdown)
    {
        value["markdown"] = markdown;
        return value;
    }

    public static JsonObject Text(string key, string value) =>
        new() { ["key"] = key, ["name"] = key, ["format"] = "text", ["text"] = value };

    public static JsonObject Number(string key, double value) =>
        new() { ["key"] = key, ["name"] = key, ["format"] = "number", ["number"] = value };

    public static JsonObject Checkbox(string key, bool value) =>
        new() { ["key"] = key, ["name"] = key, ["format"] = "checkbox", ["checkbox"] = value };

    public static JsonObject Select(string key, string optionKey, string optionName) => new()
    {
        ["key"] = key, ["name"] = key, ["format"] = "select",
        ["select"] = new JsonObject { ["id"] = "tag-" + optionKey, ["key"] = optionKey, ["name"] = optionName },
    };

    public static JsonObject Objects(string key, params string[] ids) => new()
    {
        ["key"] = key, ["name"] = key, ["format"] = "objects",
        ["objects"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
    };
}
