using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// One thing the builder will do (or, in a check, would do). Kind is a stable word;
// Key says on what; Detail says why in a sentence a person reads. The planner
// only describes: the executor makes the calls.
internal sealed record PlanStep(string Kind, string Key, string Detail, JsonObject? Data = null);

// What a plan still has to do in a space, given what the space holds now, as the
// reader returned it. Pure: the same plan and the same live read give the same
// steps, which is what lets a check run with no write and a preview be trusted.
//
// The order of the steps is the order of creation: a collection or a query comes
// before the templates and pages whose in-body views point at it, a template
// before the type that makes it its default, every type's panel before its views.
internal static class SpacePlanPlanner
{
    public static IReadOnlyList<PlanStep> Plan(
        SpacePlan plan,
        JsonObject live,
        ISet<string> existingObjects,
        bool includeObjects,
        bool includeWidgets,
        bool includeSchema = false,
        bool prune = false)
    {
        var steps = new List<PlanStep>();
        JsonArray liveTypes = live["types"] as JsonArray ?? [];

        JsonObject? LiveType(string key) =>
            liveTypes.OfType<JsonObject>().FirstOrDefault(t => Str(t, "key") == key);

        // 0. Colours of options that exist. The schema stage creates an option with
        // its colour and never touches one that exists; v2 has no way to recolour.
        if (includeSchema)
            OptionColourSteps(steps, plan, live);

        // 1. Panels.
        foreach (TypeSpec type in plan.Manifest.Types)
        {
            if (!plan.TypeExtras.TryGetValue(type.Key, out PlanTypeExtras? extras)) continue;
            JsonObject? liveType = LiveType(type.Key);
            if (extras.Featured.Count == 0 && extras.Hidden.Count == 0 && !HasPanel(plan, type.Key)) continue;
            PanelSteps(steps, type.Key, "featured", extras.Featured, Strings(liveType?["panel"]?["featured"]));
            PanelSteps(steps, type.Key, "hidden", extras.Hidden, Strings(liveType?["panel"]?["hidden"]));
        }

        // 1b. How each type draws its objects: header in a list or inline, page width.
        foreach (TypeSpec type in plan.Manifest.Types)
        {
            if (!plan.TypeExtras.TryGetValue(type.Key, out PlanTypeExtras? extras) || extras.Display is null) continue;
            PlanTypeDisplay liveDisplay = PlanTypeDisplay.Parse(type.Key, LiveType(type.Key)?["display"])
                ?? PlanTypeDisplay.FromWire(null);
            JsonObject set = extras.Display.Differences(liveDisplay);
            if (set.Count > 0)
                steps.Add(new PlanStep("type_display", type.Key,
                    $"type {type.Key} : affichage {string.Join(", ", set.Select(kv => $"{kv.Key} = {kv.Value}"))}",
                    new JsonObject { ["type"] = type.Key, ["set"] = set }));
        }

        // 2. Collections, then queries: bodies and widgets point at them.
        JsonArray liveCollections = live["collections"] as JsonArray ?? [];
        foreach (JsonObject collection in plan.Collections)
        {
            string name = Str(collection, "name");
            JsonObject? existing = liveCollections.OfType<JsonObject>().FirstOrDefault(c => Str(c, "name") == name);
            if (existing is null)
                steps.Add(new PlanStep("collection_create", name, $"collection « {name} » absente", collection));
            else
            {
                var missing = Strings(collection["items"]).Except(Strings(existing["items"])).ToList();
                if (missing.Count > 0)
                    steps.Add(new PlanStep("collection_add", name,
                        $"collection « {name} » : {missing.Count} membre(s) manquant(s) : {string.Join(", ", missing)}", collection));
            }
            ViewSteps(steps, "collection:" + name, collection["views"] as JsonArray, existing?["views"] as JsonArray);
        }

        JsonArray liveQueries = live["queries"] as JsonArray ?? [];
        foreach (JsonObject query in plan.Queries)
        {
            string name = Str(query, "name");
            JsonObject? existing = liveQueries.OfType<JsonObject>().FirstOrDefault(q => Str(q, "name") == name);
            if (existing is null)
                steps.Add(new PlanStep("query_create", name, $"requête « {name} » absente", query));
            else
            {
                var differences = PlanJson.Differences(query["source"], existing["source"], "source");
                if (differences.Count > 0)
                    steps.Add(new PlanStep("query_source_differs", name,
                        $"requête « {name} » : source différente, que l'API ne réécrit pas ({string.Join("; ", differences)})"));
            }
            ViewSteps(steps, "query:" + name, query["views"] as JsonArray, existing?["views"] as JsonArray);
        }

        // 3. Templates, then each type's default.
        JsonArray liveTemplates = live["templates"] as JsonArray ?? [];
        var rebuilt = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject template in plan.Templates)
        {
            string name = Str(template, "name");
            string forKey = Str(template, "for");
            JsonObject? existing = liveTemplates.OfType<JsonObject>()
                .FirstOrDefault(t => Str(t, "name") == name && Str(t, "for") == forKey);
            if (existing is null)
            {
                steps.Add(new PlanStep("template_create", $"{forKey}/{name}", $"modèle « {name} » ({forKey}) absent", template));
                rebuilt.Add($"{forKey}/{name}");
                continue;
            }
            var differences = PlanJson.Differences(Wanted(template), existing, "");
            if (differences.Count > 0)
            {
                steps.Add(new PlanStep("template_replace", $"{forKey}/{name}",
                    $"modèle « {name} » ({forKey}) : {string.Join("; ", differences.Take(3))}", template));
                rebuilt.Add($"{forKey}/{name}");
            }
        }
        foreach (TypeSpec type in plan.Manifest.Types)
        {
            if (!plan.TypeExtras.TryGetValue(type.Key, out PlanTypeExtras? extras) || extras.DefaultTemplate is null) continue;
            string liveDefault = Str(LiveType(type.Key), "default_template");
            if (liveDefault != extras.DefaultTemplate || rebuilt.Contains($"{type.Key}/{extras.DefaultTemplate}"))
                steps.Add(new PlanStep("type_default_template", type.Key,
                    $"type {type.Key} : modèle par défaut « {extras.DefaultTemplate} » (vu « {liveDefault} »)",
                    new JsonObject { ["type"] = type.Key, ["template"] = extras.DefaultTemplate }));
        }

        // 4. Type views.
        foreach (TypeSpec type in plan.Manifest.Types)
        {
            if (!plan.TypeExtras.TryGetValue(type.Key, out PlanTypeExtras? extras) || extras.Views is null) continue;
            ViewSteps(steps, "type:" + type.Key, extras.Views, LiveType(type.Key)?["views"] as JsonArray);
        }

        // 5. Pages. A page that differs has its body rewritten in place, in one
        // edit: the page keeps its id, so the links and widgets that point at it
        // still do.
        JsonArray livePages = live["pages"] as JsonArray ?? [];
        foreach (JsonObject page in plan.Pages)
        {
            string name = Str(page, "name");
            JsonObject? existing = livePages.OfType<JsonObject>().FirstOrDefault(p => Str(p, "name") == name);
            if (existing is null)
            {
                steps.Add(new PlanStep("page_create", name, $"page « {name} » absente", page));
                continue;
            }
            var differences = PlanJson.Differences(page["blocks"], existing["blocks"], "blocks");
            if (differences.Count > 0)
                steps.Add(new PlanStep("page_rewrite", name,
                    $"page « {name} » : corps réécrit ({string.Join("; ", differences.Take(3))})", page));
        }

        // 6. Objects, only when asked for.
        if (includeObjects)
            foreach (JsonObject obj in plan.Objects)
            {
                string key = Str(obj, "type") + "|" + Str(obj, "name");
                if (!existingObjects.Contains(key))
                    steps.Add(new PlanStep("object_create", key, $"objet « {Str(obj, "name")} » ({Str(obj, "type")}) absent", obj));
            }

        // 7. The sidebar, owner only.
        if (includeWidgets)
        {
            JsonArray liveWidgets = live["widgets"] as JsonArray ?? [];
            foreach (JsonObject widget in plan.Widgets)
            {
                string target = Str(widget, "target");
                JsonObject? existing = liveWidgets.OfType<JsonObject>().FirstOrDefault(w => Str(w, "target") == target);
                if (existing is null)
                    steps.Add(new PlanStep("widget_create", target, $"widget « {target} » absent", widget));
                else
                {
                    var differences = PlanJson.Differences(widget, existing);
                    if (differences.Count > 0)
                        steps.Add(new PlanStep("widget_update", target, $"widget « {target} » : {string.Join("; ", differences)}", widget));
                }
            }
        }

        // 8. Prune, only when asked: what the space holds and a stated section of the
        // plan leaves out. Last, and in reverse order of creation, so nothing is
        // removed while something still points at it.
        if (prune)
            PruneSteps(steps, plan, live, includeSchema, includeWidgets);

        return steps;
    }

    // Types and properties the space brings itself: never removed, even when a
    // plan leaves them out, since the app relies on them.
    private static readonly HashSet<string> NativeTypes =
        new(SpacePlanReader.SystemTypeKeys.Concat(["project", "task"]), StringComparer.Ordinal);

    private static void OptionColourSteps(List<PlanStep> steps, SpacePlan plan, JsonObject live)
    {
        JsonArray liveProperties = live["properties"] as JsonArray ?? [];
        foreach (PropertySpec property in plan.Manifest.Properties)
        {
            JsonObject? liveProperty = liveProperties.OfType<JsonObject>().FirstOrDefault(p => Str(p, "key") == property.Key);
            if (liveProperty is null) continue;
            foreach (TagSpec tag in property.Tags.Where(t => t.Color is not null))
            {
                JsonObject? liveTag = (liveProperty["tags"] as JsonArray ?? []).OfType<JsonObject>()
                    .FirstOrDefault(t => Str(t, "name") == tag.Name);
                if (liveTag is null || Str(liveTag, "color") == tag.Color) continue;
                steps.Add(new PlanStep("option_color", $"{property.Key}:{tag.Name}",
                    $"propriété {property.Key} : option « {tag.Name} » en {tag.Color} (vue en « {Str(liveTag, "color")} »)",
                    new JsonObject { ["property"] = property.Key, ["name"] = tag.Name, ["color"] = tag.Color }));
            }
        }
    }

    private static void PruneSteps(List<PlanStep> steps, SpacePlan plan, JsonObject live, bool includeSchema, bool includeWidgets)
    {
        if (includeWidgets && plan.States("widgets"))
        {
            var wanted = plan.Widgets.Select(w => Str(w, "target")).ToHashSet(StringComparer.Ordinal);
            foreach (JsonObject widget in (live["widgets"] as JsonArray ?? []).OfType<JsonObject>())
                if (!wanted.Contains(Str(widget, "target")))
                    steps.Add(new PlanStep("widget_delete", Str(widget, "target"),
                        $"widget « {Str(widget, "target")} » absent du plan", widget));
        }

        if (plan.States("collections"))
        {
            JsonArray liveCollections = live["collections"] as JsonArray ?? [];
            foreach (JsonObject collection in plan.Collections)
            {
                JsonObject? existing = liveCollections.OfType<JsonObject>().FirstOrDefault(c => Str(c, "name") == Str(collection, "name"));
                var extra = Strings(existing?["items"]).Except(Strings(collection["items"])).ToList();
                if (extra.Count > 0)
                    steps.Add(new PlanStep("collection_remove", Str(collection, "name"),
                        $"collection « {Str(collection, "name")} » : {extra.Count} membre(s) en trop : {string.Join(", ", extra)}",
                        new JsonObject { ["name"] = Str(collection, "name"), ["items"] = new JsonArray(extra.Select(e => (JsonNode?)e).ToArray()) }));
            }
        }

        // Objects: templates, queries, collections. The API bins only what the key
        // that deletes created; the others are reported as refused when applied.
        if (plan.States("templates"))
            foreach (JsonObject template in (live["templates"] as JsonArray ?? []).OfType<JsonObject>())
                if (!plan.Templates.Any(t => Str(t, "name") == Str(template, "name") && Str(t, "for") == Str(template, "for")))
                    steps.Add(new PlanStep("template_delete", $"{Str(template, "for")}/{Str(template, "name")}",
                        $"modèle « {Str(template, "name")} » ({Str(template, "for")}) absent du plan",
                        new JsonObject { ["name"] = Str(template, "name"), ["for"] = Str(template, "for") }));
        foreach (string section in new[] { "queries", "collections" })
        {
            if (!plan.States(section)) continue;
            IReadOnlyList<JsonObject> wanted = section == "queries" ? plan.Queries : plan.Collections;
            foreach (JsonObject item in (live[section] as JsonArray ?? []).OfType<JsonObject>())
                if (!wanted.Any(w => Str(w, "name") == Str(item, "name")))
                    steps.Add(new PlanStep("object_delete", Str(item, "name"),
                        $"{(section == "queries" ? "requête" : "collection")} « {Str(item, "name")} » absente du plan",
                        new JsonObject { ["kind"] = section == "queries" ? "query" : "collection", ["name"] = Str(item, "name") }));
        }

        if (!includeSchema) return;

        if (plan.States("types"))
            foreach (JsonObject type in (live["types"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string key = Str(type, "key");
                if (!NativeTypes.Contains(key) && plan.Manifest.Types.All(t => t.Key != key))
                    steps.Add(new PlanStep("type_delete", key, $"type {key} absent du plan", new JsonObject { ["type"] = key }));
            }

        if (!plan.States("properties")) return;
        foreach (JsonObject property in (live["properties"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string key = Str(property, "key");
            PropertySpec? wanted = plan.Manifest.Properties.FirstOrDefault(p => p.Key == key);
            if (wanted is null)
            {
                if (!KnownProperties.IsKnown(key))
                    steps.Add(new PlanStep("property_delete", key, $"propriété {key} absente du plan", new JsonObject { ["property"] = key }));
                continue;
            }
            // Options: only a property that lists its own is pruned; one with no
            // `tags` says nothing about them.
            if (wanted.Tags.Count == 0) continue;
            foreach (JsonObject tag in (property["tags"] as JsonArray ?? []).OfType<JsonObject>())
                if (wanted.Tags.All(t => t.Name != Str(tag, "name")))
                    steps.Add(new PlanStep("option_delete", $"{key}:{Str(tag, "name")}",
                        $"propriété {key} : option « {Str(tag, "name")} » absente du plan",
                        new JsonObject { ["property"] = key, ["name"] = Str(tag, "name") }));
        }
    }

    // The template members a plan states, in the shape the reader returns.
    private static JsonObject Wanted(JsonObject template)
    {
        var wanted = new JsonObject();
        if (template["blocks"] is { } blocks) wanted["blocks"] = blocks.DeepClone();
        return wanted;
    }

    private static bool HasPanel(SpacePlan plan, string typeKey) =>
        plan.TypeExtras.TryGetValue(typeKey, out PlanTypeExtras? extras) && (extras.Featured.Count > 0 || extras.Hidden.Count > 0);

    private static void PanelSteps(
        List<PlanStep> steps, string typeKey, string section, IReadOnlyList<string> wanted, IReadOnlyList<string> live)
    {
        foreach (string key in wanted.Where(k => !live.Contains(k, StringComparer.Ordinal)))
            steps.Add(new PlanStep("type_panel_add", $"{typeKey}:{key}",
                $"type {typeKey} : « {key} » passe en section {section}",
                new JsonObject { ["type"] = typeKey, ["property"] = key, ["section"] = section }));
        foreach (string key in live.Where(k => !wanted.Contains(k, StringComparer.Ordinal)))
            steps.Add(new PlanStep("type_panel_remove", $"{typeKey}:{key}",
                $"type {typeKey} : « {key} » quitte la section {section}",
                new JsonObject { ["type"] = typeKey, ["property"] = key }));
        var common = wanted.Where(k => live.Contains(k, StringComparer.Ordinal)).ToList();
        if (!common.SequenceEqual(live.Where(k => wanted.Contains(k, StringComparer.Ordinal)), StringComparer.Ordinal))
            steps.Add(new PlanStep("type_panel_order", $"{typeKey}:{section}",
                $"type {typeKey} : ordre de la section {section} : {string.Join(", ", wanted)}",
                new JsonObject { ["type"] = typeKey, ["section"] = section, ["order"] = new JsonArray(wanted.Select(w => (JsonNode?)w).ToArray()) }));
    }

    // One step per host whose views differ. The executor reads the host's views
    // again just before writing and works from that, so the step only says that a
    // view is missing, different, extra or out of order.
    private static void ViewSteps(List<PlanStep> steps, string host, JsonArray? wanted, JsonArray? live)
    {
        if (wanted is null) return;
        var wantedViews = wanted.OfType<JsonObject>().ToList();
        var liveViews = (live ?? []).OfType<JsonObject>().ToList();
        var reasons = new List<string>();
        foreach (JsonObject view in wantedViews)
        {
            string name = Str(view, "name");
            JsonObject? found = liveViews.FirstOrDefault(v => Str(v, "name") == name);
            if (found is null) { reasons.Add($"vue « {name} » absente"); continue; }
            var differences = PlanViewsDiff.Differences(view, found);
            if (differences.Count > 0) reasons.Add($"vue « {name} » : {string.Join("; ", differences.Take(3))}");
        }
        foreach (JsonObject view in liveViews.Where(v => !wantedViews.Any(w => Str(w, "name") == Str(v, "name"))))
            reasons.Add($"vue « {Str(view, "name")} » en trop");
        var common = wantedViews.Select(v => Str(v, "name")).Where(n => liveViews.Any(l => Str(l, "name") == n)).ToList();
        if (!common.SequenceEqual(liveViews.Select(v => Str(v, "name")).Where(n => common.Contains(n))))
            reasons.Add("ordre des vues");
        if (reasons.Count == 0) return;
        steps.Add(new PlanStep("views", host, $"{host} : {string.Join(" | ", reasons)}",
            new JsonObject { ["host"] = host, ["views"] = wanted.DeepClone() }));
    }

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        (node as JsonArray ?? []).Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList();

    private static string Str(JsonObject? node, string key) => node?[key]?.GetValue<string>() ?? "";
}
