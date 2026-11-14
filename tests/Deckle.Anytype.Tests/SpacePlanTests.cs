using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The pure half of the space plan builder: parsing, validation, the two shapes of
// a view, the comparison, and the planner's order. Nothing here calls Anytype;
// the calls are proven by the explicit trial tests on a throwaway space.
public sealed class SpacePlanTests
{
    // One small plan that touches every section.
    private static JsonObject SamplePlan() => JsonNode.Parse("""
    {
      "plan_version": 1,
      "properties": [
        { "key": "state", "name": "State", "format": "select", "tags": [ { "name": "Open", "color": "lime" }, { "name": "Done", "color": "grey" } ] },
        { "key": "owner_note", "name": "Owner note", "format": "text" },
        { "key": "projects", "name": "Projects", "format": "objects" }
      ],
      "types": [
        { "key": "ticket", "name": "Ticket", "plural_name": "Tickets", "layout": "basic",
          "properties": [ "state", "owner_note", "projects" ],
          "panel": { "featured": [ "state" ], "hidden": [ "owner_note" ] },
          "default_template": "Ticket default",
          "views": [
            { "name": "Open", "type": "table",
              "filters": [ { "property": "state", "condition": "in", "value": [ "Open" ] } ],
              "sorts": [ { "property": "name", "direction": "asc" } ],
              "columns": [ { "property": "name", "width": 300 }, { "property": "state" } ] },
            { "name": "Board", "type": "kanban", "group_by": "state", "columns": [ { "property": "name" } ] }
          ] }
      ],
      "templates": [
        { "name": "Ticket default", "for": "ticket",
          "blocks": [
            { "type": "heading_2", "text": "Notes" },
            { "type": "dataview", "source": "type:ticket",
              "views": [ { "name": "Siblings", "type": "list",
                "filters": [ { "property": "projects", "condition": "in", "value": "$self" } ],
                "columns": [ { "property": "name" } ] } ] }
          ] }
      ],
      "collections": [ { "name": "All tickets", "items": [ "type:ticket" ], "views": [ { "name": "All", "type": "table", "columns": [ { "property": "name" } ] } ] } ],
      "queries": [ { "name": "Open tickets", "source": { "types": [ "ticket" ] } } ]
    }
    """)!.AsObject();

    private sealed class FakeRefs : IPlanRefs
    {
        public string TypeId(string key) => "id-of-" + key;
        public string? TypeKey(string id) => id.StartsWith("id-of-", StringComparison.Ordinal) ? id[6..] : null;
        public string ObjectId(string name) => "obj-" + name;
        public string? ObjectName(string id) => id.StartsWith("obj-", StringComparison.Ordinal) ? id[4..] : null;
    }

    [Fact]
    public void Parse_SplitsTheManifestFromTheTypeExtras()
    {
        SpacePlan plan = SpacePlan.Parse(SamplePlan());

        Assert.Equal(3, plan.Manifest.Properties.Count);
        TypeSpec type = Assert.Single(plan.Manifest.Types);
        // A hidden property is out of the regular list the schema stage compares,
        // and still one of the type's own for the plan.
        Assert.Equal(["state", "projects"], type.Properties);
        Assert.Equal(["state", "owner_note", "projects"], plan.TypeExtras["ticket"].AllProperties);
        PlanTypeExtras extras = plan.TypeExtras["ticket"];
        Assert.Equal(["state"], extras.Featured);
        Assert.Equal(["owner_note"], extras.Hidden);
        Assert.Equal("Ticket default", extras.DefaultTemplate);
        Assert.Equal(2, extras.Views!.Count);
        // What the schema stage reads carries none of the plan-only members.
        Assert.Null(plan.ManifestNode["templates"]);
        Assert.Null(plan.ManifestNode["types"]![0]!["views"]);
    }

    // Anytype lists a type's header (featured) properties before its regular ones.
    // A plan that declares a header property after a regular one must still match
    // the live list, or the schema stage rewrites the order on every run.
    [Fact, Trait("Category", "regression")]
    public void Parse_PutsHeaderPropertiesFirstInTheListTheSchemaStageCompares()
    {
        JsonObject plan = SamplePlan();
        JsonObject type = plan["types"]![0]!.AsObject();
        type["properties"] = new JsonArray("projects", "owner_note", "state");

        SpacePlan parsed = SpacePlan.Parse(plan);

        Assert.Equal(["state", "projects"], Assert.Single(parsed.Manifest.Types).Properties);
        Assert.Equal(["projects", "owner_note", "state"], parsed.TypeExtras["ticket"].AllProperties);
    }

    [Fact]
    public void Parse_RefusesAPlanVersionItDoesNotRead()
    {
        JsonObject root = SamplePlan();
        root["plan_version"] = 2;
        Assert.Throws<ArgumentException>(() => SpacePlan.Parse(root));
    }

    [Fact]
    public void Validate_AcceptsTheSamplePlan() =>
        Assert.Empty(SpacePlanValidator.Validate(SpacePlan.Parse(SamplePlan())));

    [Fact]
    public void Validate_NamesAViewPropertyThatExistsNowhere()
    {
        // The « Epic everywhere » trap: a column on a property nobody declared.
        JsonObject root = SamplePlan();
        root["types"]![0]!["views"]![0]!["columns"]!.AsArray().Add(new JsonObject { ["property"] = "epic" });
        IReadOnlyList<string> problems = SpacePlanValidator.Validate(SpacePlan.Parse(root));
        Assert.Contains(problems, p => p.Contains("« epic »"));
    }

    [Fact]
    public void Validate_RefusesAnOptionTheFilterPropertyLacks()
    {
        JsonObject root = SamplePlan();
        root["types"]![0]!["views"]![0]!["filters"]![0]!["value"] = new JsonArray("Blocked");
        Assert.Contains(SpacePlanValidator.Validate(SpacePlan.Parse(root)), p => p.Contains("Blocked"));
    }

    [Fact]
    public void Validate_RefusesALastOpenedColumn()
    {
        JsonObject root = SamplePlan();
        root["types"]![0]!["views"]![0]!["columns"]!.AsArray().Add(new JsonObject { ["property"] = "lastOpenedDate" });
        Assert.Contains(SpacePlanValidator.Validate(SpacePlan.Parse(root)), p => p.Contains("lastOpenedDate"));
    }

    [Fact]
    public void Validate_RefusesAnEmptyViewShell()
    {
        // « Pièce par défaut » of Home: a view block with no source.
        JsonObject root = SamplePlan();
        root["templates"]![0]!["blocks"]![1]!.AsObject().Remove("source");
        Assert.Contains(SpacePlanValidator.Validate(SpacePlan.Parse(root)), p => p.Contains("source"));
    }

    [Fact]
    public void Validate_RefusesAPanelPropertyOutsideTheTypeAndAMissingDefaultTemplate()
    {
        JsonObject root = SamplePlan();
        root["types"]![0]!["panel"]!["featured"] = new JsonArray("projects_typo");
        root["types"]![0]!["default_template"] = "Nope";
        IReadOnlyList<string> problems = SpacePlanValidator.Validate(SpacePlan.Parse(root));
        Assert.Contains(problems, p => p.Contains("panel"));
        Assert.Contains(problems, p => p.Contains("Nope"));
    }

    [Fact]
    public void Validate_RefusesTooManySortsForTheApi()
    {
        JsonObject root = SamplePlan();
        var sorts = new JsonArray();
        for (int i = 0; i < 11; i++) sorts.Add(new JsonObject { ["property"] = "name" });
        root["types"]![0]!["views"]![0]!["sorts"] = sorts;
        Assert.Contains(SpacePlanValidator.Validate(SpacePlan.Parse(root)), p => p.Contains("11 tris"));
    }

    [Fact]
    public void ToWire_GivesEverySortAndFilterItsOwn24HexId_AndKeepsOptionNames()
    {
        JsonObject view = SamplePlan()["types"]![0]!["views"]![0]!.AsObject();
        view["sorts"]!.AsArray().Add(new JsonObject { ["property"] = "state", ["direction"] = "desc" });

        JsonObject wire = PlanViews.ToWire(view, new FakeRefs());

        string[] ids = wire["sorts"]!.AsArray().Concat(wire["filters"]!.AsArray())
            .Select(n => n!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(3, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches("^[0-9a-f]{24}$", id));
        Assert.Equal("Open", wire["filters"]![0]!["value"]![0]!.GetValue<string>());
    }

    [Fact]
    public void ToWire_TurnsSelfIntoThePlaceholderTemplatesCarry()
    {
        JsonObject view = SamplePlan()["templates"]![0]!["blocks"]![1]!["views"]![0]!.AsObject();
        Assert.Equal(PlanViews.TemplatePlaceholder, PlanViews.ToWire(view, new FakeRefs())["filters"]![0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void FromWire_IsTheReverseOfToWire()
    {
        JsonObject view = SamplePlan()["types"]![0]!["views"]![0]!.AsObject();
        var refs = new FakeRefs();

        JsonObject back = PlanViews.FromWire(PlanViews.ToWire(view, refs), refs);

        Assert.Empty(PlanViewsDiff.Differences(view, back));
        Assert.Null(back["sorts"]![0]!["id"]);
        Assert.Equal("Open", back["filters"]![0]!["value"]![0]!.GetValue<string>());
    }

    [Fact]
    public void FromWire_LeavesOutTheColumnTheApiCannotHide()
    {
        var wire = JsonNode.Parse("""
          { "name": "All", "columns": [ { "property": "name", "width": 200 }, { "property": "lastOpenedDate", "hidden": true } ] }
          """)!.AsObject();
        JsonObject plan = PlanViews.FromWire(wire, new FakeRefs());
        Assert.Single(plan["columns"]!.AsArray());
    }

    [Fact]
    public void Body_RoundTripsADataviewSourceAndDropsBlockIds()
    {
        JsonArray body = SamplePlan()["templates"]![0]!["blocks"]!.AsArray();
        var refs = new FakeRefs();

        JsonArray wire = PlanViews.BodyToWire(body, refs);
        Assert.Equal("id-of-ticket", wire[1]!["object_id"]!.GetValue<string>());
        Assert.Null(wire[1]!["source"]);

        wire[0]!["id"] = "abc12";
        JsonArray back = PlanViews.BodyFromWire(wire, refs);
        Assert.Equal("type:ticket", back[1]!["source"]!.GetValue<string>());
        Assert.Null(back[0]!["id"]);
        Assert.Empty(PlanJson.Differences(body, back));
    }

    [Fact]
    public void Differences_IgnoresWhatThePlanLeavesSilent_AndChecksOrder()
    {
        var wanted = JsonNode.Parse("""{ "a": 1, "list": [ { "p": "x" }, { "p": "y" } ] }""");
        var live = JsonNode.Parse("""{ "a": 1, "extra": true, "list": [ { "p": "x", "w": 9 }, { "p": "y" } ] }""");
        Assert.Empty(PlanJson.Differences(wanted, live));

        var swapped = JsonNode.Parse("""{ "list": [ { "p": "y" }, { "p": "x" } ] }""");
        Assert.NotEmpty(PlanJson.Differences(wanted, swapped));
    }

    [Fact]
    public void Differences_TreatsAWantedFalseAsTheApisOmission()
    {
        var wanted = JsonNode.Parse("""{ "hide_icon": false }""");
        Assert.Empty(PlanJson.Differences(wanted, JsonNode.Parse("{}")));
    }

    [Fact]
    public void ViewsDiff_SeesAMissingGroupingAndANewSort()
    {
        JsonObject wanted = JsonNode.Parse("""{ "name": "V", "sorts": [ { "property": "name" } ] }""")!.AsObject();
        JsonObject live = JsonNode.Parse("""{ "name": "V", "type": "kanban", "group_by": "state", "sorts": [] }""")!.AsObject();
        Assert.True(PlanViewsDiff.SetDiffers(wanted, live));
        JsonObject set = PlanViewsDiff.SetPayload(wanted, live, new FakeRefs());
        Assert.True(set.ContainsKey("group_by") && set["group_by"] is null);
    }

    [Fact]
    public void ColumnOps_KeepsTheMatchingPrefix_RemovesTheRest_AndAddsInOrderOneOpEach()
    {
        JsonObject wanted = JsonNode.Parse("""{ "name": "V", "columns": [ { "property": "name" }, { "property": "c" }, { "property": "b", "hidden": true } ] }""")!.AsObject();
        JsonObject live = JsonNode.Parse("""{ "name": "V", "columns": [ { "property": "name" }, { "property": "b" }, { "property": "c" } ] }""")!.AsObject();

        List<JsonObject> ops = PlanViewsDiff.ColumnOps("v1", wanted, live);

        // name stays; b and c are removed in one op; c then b are added, one op each.
        Assert.Equal(3, ops.Count);
        Assert.Equal(["b", "c"], ops[0]["columns"]!.AsObject().Select(kv => kv.Key).Order().ToArray());
        Assert.All(ops[0]["columns"]!.AsObject(), kv => Assert.Null(kv.Value));
        Assert.Equal("c", ops[1]["columns"]!.AsObject().Single().Key);
        Assert.Equal("b", ops[2]["columns"]!.AsObject().Single().Key);
        Assert.True(ops[2]["columns"]!["b"]!["hidden"]!.GetValue<bool>());
    }

    [Fact]
    public void ColumnOps_AreEmptyWhenTheColumnsAlreadyMatch()
    {
        JsonObject view = JsonNode.Parse("""{ "name": "V", "columns": [ { "property": "name", "width": 200 }, { "property": "b", "hidden": true } ] }""")!.AsObject();
        Assert.Empty(PlanViewsDiff.ColumnOps("v1", view, view));
    }

    // The live side the planner reads is the plan's own shape. A space that is
    // exactly the plan needs no step.
    private static JsonObject LiveEqualTo(JsonObject plan)
    {
        var live = new JsonObject();
        JsonObject type = (JsonObject)plan["types"]![0]!.DeepClone();
        live["types"] = new JsonArray(type);
        live["templates"] = plan["templates"]!.DeepClone();
        live["collections"] = plan["collections"]!.DeepClone();
        live["queries"] = plan["queries"]!.DeepClone();
        return live;
    }

    [Fact]
    public void Planner_AsksForNothingWhenTheSpaceIsThePlan()
    {
        JsonObject root = SamplePlan();
        SpacePlan plan = SpacePlan.Parse(root);
        JsonObject live = LiveEqualTo(root);
        // The reader reports the default template by name on the type and the
        // queries/collections with their (here absent) views.
        live["queries"]![0]!.AsObject()["views"] = new JsonArray();
        root["queries"]![0]!.AsObject().Remove("views");

        IReadOnlyList<PlanStep> steps = SpacePlanPlanner.Plan(plan, live, new HashSet<string>(), false, false);

        Assert.Empty(steps);
    }

    [Fact]
    public void Planner_OrdersAnEmptySpaceByCreation()
    {
        SpacePlan plan = SpacePlan.Parse(SamplePlan());

        IReadOnlyList<PlanStep> steps = SpacePlanPlanner.Plan(plan, new JsonObject(), new HashSet<string>(), false, false);
        string[] kinds = steps.Select(s => s.Kind).ToArray();

        int First(string kind) => Array.IndexOf(kinds, kind);
        int typeViews = steps.ToList().FindIndex(s => s.Kind == "views" && s.Key == "type:ticket");
        Assert.True(First("type_panel_add") < First("collection_create"));
        Assert.True(First("collection_create") < First("query_create"));
        Assert.True(First("query_create") < First("template_create"));
        Assert.True(First("template_create") < First("type_default_template"));
        Assert.True(First("type_default_template") < typeViews);
    }

    [Fact]
    public void Planner_ReplacesATemplateWhoseBodyDiffers_AndRepointsTheDefault()
    {
        JsonObject root = SamplePlan();
        SpacePlan plan = SpacePlan.Parse(root);
        JsonObject live = LiveEqualTo(root);
        live["templates"]![0]!["blocks"]![0]!["text"] = "Old heading";
        live["queries"]![0]!.AsObject()["views"] = new JsonArray();
        root["queries"]![0]!.AsObject().Remove("views");

        IReadOnlyList<PlanStep> steps = SpacePlanPlanner.Plan(plan, live, new HashSet<string>(), false, false);

        Assert.Contains(steps, s => s.Kind == "template_replace");
        Assert.Contains(steps, s => s.Kind == "type_default_template");
    }

    [Fact]
    public void Planner_AddsObjectsAndWidgetsOnlyWhenAsked()
    {
        JsonObject root = SamplePlan();
        root["objects"] = JsonNode.Parse("""[ { "type": "ticket", "name": "First", "template": "Ticket default" } ]""");
        root["widgets"] = JsonNode.Parse("""[ { "target": "type:ticket", "layout": "view", "view": "Open" } ]""");
        SpacePlan plan = SpacePlan.Parse(root);

        Assert.DoesNotContain(SpacePlanPlanner.Plan(plan, new JsonObject(), new HashSet<string>(), false, false),
            s => s.Kind is "object_create" or "widget_create");
        IReadOnlyList<PlanStep> all = SpacePlanPlanner.Plan(plan, new JsonObject(), new HashSet<string>(), true, true);
        Assert.Contains(all, s => s.Kind == "object_create");
        Assert.Contains(all, s => s.Kind == "widget_create");
    }

    [Fact]
    public void Planner_SkipsAnObjectThatExists()
    {
        JsonObject root = SamplePlan();
        root["objects"] = JsonNode.Parse("""[ { "type": "ticket", "name": "First" } ]""");
        SpacePlan plan = SpacePlan.Parse(root);
        IReadOnlyList<PlanStep> steps = SpacePlanPlanner.Plan(plan, new JsonObject(), new HashSet<string> { "ticket|First" }, true, false);
        Assert.DoesNotContain(steps, s => s.Kind == "object_create");
    }
}
