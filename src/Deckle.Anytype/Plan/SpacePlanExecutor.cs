using System.Net.Http;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Makes the calls a list of plan steps stands for. One step, one or a few
// requests, each logged as one line. Nothing here decides what to do: the planner
// did; the executor reads what it needs again just before it writes (a view's
// current ids and columns), because ids only exist once the thing does.
internal sealed class SpacePlanExecutor(
    AnytypeApiClient api,
    PlanApi v2,
    SpacePlanRead live)
{
    private readonly LivePlanRefs refs = live.Refs;

    private readonly Dictionary<string, string> _templateIds = live.Templates
        .GroupBy(t => t.For + "/" + t.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

    public async Task RunAsync(IReadOnlyList<PlanStep> steps, List<string> log, CancellationToken ct)
    {
        foreach (PlanStep step in steps)
        {
            switch (step.Kind)
            {
                case "type_panel_add": await PatchTypeAsync(step, "add_property", log, ct); break;
                case "type_panel_remove": await PatchTypeAsync(step, "remove_property", log, ct); break;
                case "type_panel_order": await OrderPanelAsync(step, log, ct); break;
                case "collection_create": await CreateCollectionAsync(step, log, ct); break;
                case "collection_add": await AddToCollectionAsync(step, log, ct); break;
                case "query_create": await CreateQueryAsync(step, log, ct); break;
                case "template_create": await CreateTemplateAsync(step, log, ct); break;
                case "template_replace": await ReplaceTemplateAsync(step, log, ct); break;
                case "type_default_template": await SetDefaultTemplateAsync(step, log, ct); break;
                case "views": await EnsureViewsAsync(step, log, ct); break;
                case "page_create": await CreatePageAsync(step, log, ct); break;
                case "object_create": await CreateObjectAsync(step, log, ct); break;
                case "widget_create": await CreateWidgetAsync(step, log, ct); break;
                case "widget_update": await UpdateWidgetAsync(step, log, ct); break;
                case "option_color": await RecolourOptionAsync(step, log, ct); break;
                case "type_display": await SetTypeDisplayAsync(step, log, ct); break;
                case "page_rewrite": await RewritePageAsync(step, log, ct); break;
                case "widget_delete": await DeleteWidgetAsync(step, log, ct); break;
                case "collection_remove": await RemoveFromCollectionAsync(step, log, ct); break;
                case "template_delete":
                    await RefusableAsync(step, log, () => v2.SendAsync(HttpMethod.Delete,
                        $"objects/{_templateIds[Str(step.Data, "for") + "/" + Str(step.Data, "name")]}", null, ct)).ConfigureAwait(false);
                    break;
                case "object_delete":
                    await RefusableAsync(step, log, () => v2.SendAsync(HttpMethod.Delete,
                        $"objects/{refs.ObjectId(Str(step.Data, "name"))}", null, ct)).ConfigureAwait(false);
                    break;
                case "type_delete":
                    await RefusableAsync(step, log, () => v2.SendAsync(HttpMethod.Delete,
                        $"types/{Uri.EscapeDataString(Str(step.Data, "type"))}", null, ct)).ConfigureAwait(false);
                    break;
                case "property_delete":
                    await RefusableAsync(step, log, () => v2.SendAsync(HttpMethod.Delete,
                        $"properties/{Uri.EscapeDataString(Str(step.Data, "property"))}", null, ct)).ConfigureAwait(false);
                    break;
                case "option_delete": await DeleteOptionAsync(step, log, ct); break;
                case "query_source_differs":
                    log.Add($"laissé tel quel : {step.Detail}");
                    break;
                default:
                    throw new InvalidOperationException($"Étape inconnue « {step.Kind} ».");
            }
        }
    }

    // ── Panels ──────────────────────────────────────────────────────────────

    private async Task PatchTypeAsync(PlanStep step, string op, List<string> log, CancellationToken ct)
    {
        JsonObject data = step.Data!;
        var body = new JsonObject { ["op"] = op, ["property"] = data["property"]!.DeepClone() };
        if (op == "add_property") body["section"] = data["section"]!.DeepClone();
        await v2.PatchOpsAsync($"types/{Str(data, "type")}", [body], ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task OrderPanelAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject data = step.Data!;
        var ops = new List<JsonObject>();
        string? previous = null;
        foreach (JsonNode? node in data["order"] as JsonArray ?? [])
        {
            string property = node!.GetValue<string>();
            var op = new JsonObject { ["op"] = "move_property", ["property"] = property };
            if (previous is null) op["position"] = "first"; else op["after"] = previous;
            ops.Add(op);
            previous = property;
        }
        if (ops.Count > 0)
            await v2.PatchOpsAsync($"types/{Str(data, "type")}", ops, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // ── Collections and queries ─────────────────────────────────────────────

    private string ResolveMember(string symbol) =>
        symbol.StartsWith("type:", StringComparison.Ordinal) ? refs.TypeId(symbol["type:".Length..])
        : symbol.StartsWith("object:", StringComparison.Ordinal) ? refs.ObjectId(symbol["object:".Length..])
        : symbol;

    private async Task CreateCollectionAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject collection = step.Data!;
        var items = new JsonArray();
        foreach (JsonNode? item in collection["items"] as JsonArray ?? [])
            items.Add(ResolveMember(item!.GetValue<string>()));
        JsonObject made = await v2.SendAsync(
            HttpMethod.Post, "collections", new JsonObject { ["name"] = Str(collection, "name"), ["items"] = items }, ct)
            .ConfigureAwait(false);
        refs.SetObject(Str(collection, "name"), SchemaApiJson.Id(made));
        log.Add(step.Detail);
    }

    private async Task AddToCollectionAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject collection = step.Data!;
        string id = refs.ObjectId(Str(collection, "name"));
        var ids = (collection["items"] as JsonArray ?? []).Select(i => ResolveMember(i!.GetValue<string>())).ToList();
        await api.AddToCollectionAsync(v2.SpaceId, id, ids, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task CreateQueryAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject query = step.Data!;
        JsonArray types = query["source"]?["types"] as JsonArray ?? [];
        if (types.Count != 1 || query["source"]?["properties"] is JsonArray { Count: > 0 })
            throw new InvalidOperationException(
                $"Requête « {Str(query, "name")} » : l'API crée une requête sur un seul type ; sa source en nomme {types.Count}.");
        JsonObject made = await v2.SendAsync(
            HttpMethod.Post, "queries",
            new JsonObject { ["name"] = Str(query, "name"), ["type"] = types[0]!.GetValue<string>() }, ct)
            .ConfigureAwait(false);
        refs.SetObject(Str(query, "name"), SchemaApiJson.Id(made));
        log.Add(step.Detail);
    }

    // ── Templates ───────────────────────────────────────────────────────────

    private async Task<string> PostTemplateAsync(JsonObject template, CancellationToken ct)
    {
        var properties = new JsonObject { ["name"] = Str(template, "name") };
        if (template["properties"] is JsonObject presets)
            foreach ((string key, JsonNode? value) in presets) properties[key] = value?.DeepClone();
        var body = new JsonObject
        {
            ["kind"] = "template",
            ["type"] = "template",
            ["template_for"] = Str(template, "for"),
            ["properties"] = properties,
        };
        if (template["icon"] is { } icon) body["icon"] = icon.DeepClone();
        if (template["blocks"] is JsonArray blocks && blocks.Count > 0)
            body["blocks"] = PlanViews.BodyToWire(blocks, refs);
        JsonObject made = await v2.SendAsync(HttpMethod.Post, "templates", body, ct).ConfigureAwait(false);
        string id = SchemaApiJson.Id(made);
        _templateIds[Str(template, "for") + "/" + Str(template, "name")] = id;
        return id;
    }

    private async Task CreateTemplateAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        await PostTemplateAsync(step.Data!, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // The old template goes only once the new one exists, and the type's default
    // is pointed at the new one by the step that follows.
    private async Task ReplaceTemplateAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        string key = Str(step.Data, "for") + "/" + Str(step.Data, "name");
        string? oldId = _templateIds.GetValueOrDefault(key);
        await PostTemplateAsync(step.Data!, ct).ConfigureAwait(false);
        if (oldId is not null)
            await v2.SendAsync(HttpMethod.Delete, $"objects/{oldId}", null, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task SetDefaultTemplateAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        string type = Str(step.Data, "type");
        string template = Str(step.Data, "template");
        if (!_templateIds.TryGetValue(type + "/" + template, out string? id))
            throw new InvalidOperationException($"Modèle « {template} » du type « {type} » introuvable pour en faire le défaut.");
        await v2.SendAsync(HttpMethod.Patch, $"types/{type}", new JsonObject { ["default_template"] = id }, ct)
            .ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // ── Views ───────────────────────────────────────────────────────────────

    private string HostPath(string host)
    {
        string kind = host[..host.IndexOf(':')];
        string name = host[(host.IndexOf(':') + 1)..];
        return kind == "type" ? $"types/{name}" : $"objects/{refs.ObjectId(name)}";
    }

    private sealed record LiveView(string Id, JsonObject Plan);

    private async Task<(string? BlockId, List<LiveView> Views)> ReadViewsAsync(string path, CancellationToken ct)
    {
        JsonObject doc = await v2.GetAsync(path, ct).ConfigureAwait(false);
        JsonObject? dataview = (doc["blocks"] as JsonArray ?? []).OfType<JsonObject>()
            .FirstOrDefault(b => Str(b, "type") == "dataview");
        var views = new List<LiveView>();
        foreach (JsonObject wire in (dataview?["views"] as JsonArray ?? []).OfType<JsonObject>())
            views.Add(new LiveView(Str(wire, "id"), PlanViews.FromWire(wire, refs)));
        return (dataview is null ? null : Str(dataview, "id"), views);
    }

    private async Task EnsureViewsAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        string host = Str(step.Data, "host");
        string path = HostPath(host);
        var wanted = (step.Data!["views"] as JsonArray ?? []).OfType<JsonObject>().ToList();

        // Inserts first, so the last view is never deleted while one is missing.
        var (_, live) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
        var empty = new JsonObject();
        foreach (JsonObject view in wanted.Where(w => live.All(l => Str(l.Plan, "name") != Str(w, "name"))))
        {
            var op = new JsonObject
            {
                ["op"] = "insert_view",
                ["name"] = Str(view, "name"),
                ["position"] = "last",
                ["set"] = PlanViewsDiff.SetPayload(view, empty, refs),
            };
            await v2.PatchOpsAsync(path, [op], ct).ConfigureAwait(false);
            log.Add($"{host} : vue « {Str(view, "name")} » créée");
        }

        (_, live) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
        foreach (JsonObject view in wanted)
        {
            LiveView? found = live.FirstOrDefault(l => Str(l.Plan, "name") == Str(view, "name"));
            if (found is null) throw new InvalidOperationException($"{host} : la vue « {Str(view, "name")} » créée est introuvable à la relecture.");
            var ops = new List<JsonObject>();
            if (PlanViewsDiff.SetDiffers(view, found.Plan))
                ops.Add(new JsonObject
                {
                    ["op"] = "update_view",
                    ["view"] = found.Id,
                    ["set"] = PlanViewsDiff.SetPayload(view, found.Plan, refs),
                });
            ops.AddRange(PlanViewsDiff.ColumnOps(found.Id, view, found.Plan));
            if (ops.Count == 0) continue;
            await v2.PatchOpsAsync(path, ops, ct).ConfigureAwait(false);
            log.Add($"{host} : vue « {Str(view, "name")} » mise à jour ({ops.Count} opération(s))");
        }

        (_, live) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
        foreach (LiveView extra in live.Where(l => wanted.All(w => Str(w, "name") != Str(l.Plan, "name"))).ToList())
        {
            await v2.PatchOpsAsync(path, [new JsonObject { ["op"] = "delete_view", ["view"] = extra.Id }], ct).ConfigureAwait(false);
            log.Add($"{host} : vue « {Str(extra.Plan, "name")} » supprimée");
        }

        // Order: the first view is the tab the app opens on.
        (_, live) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < live.Count && Str(live[i].Plan, "name") == Str(wanted[i], "name")) continue;
            LiveView target = live.First(l => Str(l.Plan, "name") == Str(wanted[i], "name"));
            var op = new JsonObject { ["op"] = "move_view", ["view"] = target.Id };
            if (i == 0) op["position"] = "first"; else op["after"] = live[i - 1].Id;
            await v2.PatchOpsAsync(path, [op], ct).ConfigureAwait(false);
            (_, live) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
            log.Add($"{host} : vue « {Str(wanted[i], "name")} » placée en position {i + 1}");
        }
    }

    // ── Pages and objects ───────────────────────────────────────────────────

    private async Task CreatePageAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject page = step.Data!;
        var body = new JsonObject
        {
            ["type"] = Str(page, "type") is { Length: > 0 } t ? t : "page",
            ["template"] = "none",
            ["properties"] = new JsonObject { ["name"] = Str(page, "name") },
        };
        if (page["icon"] is { } icon) body["icon"] = icon.DeepClone();
        if (page["blocks"] is JsonArray blocks && blocks.Count > 0) body["blocks"] = PlanViews.BodyToWire(blocks, refs);
        JsonObject made = await v2.SendAsync(HttpMethod.Post, "objects", body, ct).ConfigureAwait(false);
        string id = SchemaApiJson.Id(made);
        refs.SetObject(Str(page, "name"), id);
        await ReplacePlaceholderAsync(id, log, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task CreateObjectAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject obj = step.Data!;
        string type = Str(obj, "type");
        var properties = new JsonObject { ["name"] = Str(obj, "name") };
        if (obj["properties"] is JsonObject given)
            foreach ((string key, JsonNode? value) in given) properties[key] = value?.DeepClone();
        var body = new JsonObject { ["type"] = type, ["properties"] = properties };
        string template = Str(obj, "template");
        if (template == "none") body["template"] = "none";
        else if (template.Length > 0)
            body["template"] = _templateIds.TryGetValue(type + "/" + template, out string? tid)
                ? tid : throw new InvalidOperationException($"Modèle « {template} » du type « {type} » introuvable.");
        JsonObject made = await v2.SendAsync(HttpMethod.Post, "objects", body, ct).ConfigureAwait(false);
        string id = SchemaApiJson.Id(made);
        refs.SetObject(Str(obj, "name"), id);
        await ReplacePlaceholderAsync(id, log, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // A template carries the app's placeholder in its views' filters, where the
    // app puts the new object's id when it makes one from the template. The API
    // does not, so every filter value that is still the placeholder is rewritten
    // to the object itself. Each entry keeps its id so the list does not collapse.
    public async Task ReplacePlaceholderAsync(string objectId, List<string> log, CancellationToken ct)
    {
        JsonObject doc = await v2.GetAsync($"objects/{objectId}", ct).ConfigureAwait(false);
        var ops = new List<JsonObject>();
        foreach (JsonObject block in (doc["blocks"] as JsonArray ?? []).OfType<JsonObject>().Where(b => Str(b, "type") == "dataview"))
            foreach (JsonObject view in (block["views"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (view["filters"] is not JsonArray filters || !HasPlaceholder(filters)) continue;
                var replaced = (JsonArray)filters.DeepClone();
                Replace(replaced, objectId);
                ops.Add(new JsonObject
                {
                    ["op"] = "update_view",
                    ["block"] = Str(block, "id"),
                    ["view"] = Str(view, "id"),
                    ["set"] = new JsonObject { ["filters"] = replaced },
                });
            }
        if (ops.Count == 0) return;
        await v2.PatchOpsAsync($"objects/{objectId}", ops, ct).ConfigureAwait(false);
        log.Add($"filtre « {PlanViews.TemplatePlaceholder} » remplacé par l'objet dans {ops.Count} vue(s)");
    }

    private static bool HasPlaceholder(JsonNode? node) => node switch
    {
        JsonArray array => array.Any(HasPlaceholder),
        JsonObject obj => obj.Any(kv => HasPlaceholder(kv.Value)),
        JsonValue scalar => scalar.TryGetValue<string>(out string? s) && s == PlanViews.TemplatePlaceholder,
        _ => false,
    };

    private static void Replace(JsonNode? node, string objectId)
    {
        switch (node)
        {
            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.TryGetValue<string>(out string? s) && s == PlanViews.TemplatePlaceholder)
                        array[i] = objectId;
                    else Replace(array[i], objectId);
                }
                break;
            case JsonObject obj:
                foreach (string key in obj.Select(kv => kv.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue<string>(out string? s) && s == PlanViews.TemplatePlaceholder)
                        obj[key] = objectId;
                    else Replace(obj[key], objectId);
                }
                break;
        }
    }

    // ── Widgets ─────────────────────────────────────────────────────────────

    private string WidgetTarget(string symbol) => ResolveMember(symbol);

    private async Task<string?> ViewIdAsync(string targetSymbol, string viewName, CancellationToken ct)
    {
        string kind = targetSymbol[..Math.Max(0, targetSymbol.IndexOf(':'))];
        if (kind is not ("type" or "object")) return null;
        string path = kind == "type" ? $"types/{targetSymbol[5..]}" : $"objects/{refs.ObjectId(targetSymbol["object:".Length..])}";
        var (_, views) = await ReadViewsAsync(path, ct).ConfigureAwait(false);
        return views.FirstOrDefault(v => Str(v.Plan, "name") == viewName)?.Id;
    }

    private async Task CreateWidgetAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject widget = step.Data!;
        string target = Str(widget, "target");
        var body = new JsonObject { ["target"] = WidgetTarget(target), ["scope"] = "space", ["position"] = "last" };
        foreach (string member in new[] { "layout", "limit" })
            if (widget[member] is { } value) body[member] = value.DeepClone();
        if (Str(widget, "view") is { Length: > 0 } viewName)
            body["view_id"] = await ViewIdAsync(target, viewName, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Widget « {target} » : la vue « {viewName} » n'existe pas sur sa cible.");
        await v2.SendAsync(HttpMethod.Post, "widgets", body, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task UpdateWidgetAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject widget = step.Data!;
        string targetId = WidgetTarget(Str(widget, "target"));
        JsonObject? row = (await v2.ListAsync("widgets", [new("scope", "space")], ct).ConfigureAwait(false))
            .FirstOrDefault(w => Str(w, "target") == targetId);
        if (row is null) throw new InvalidOperationException($"Widget « {Str(widget, "target")} » introuvable pour le mettre à jour.");
        var body = new JsonObject();
        foreach (string member in new[] { "layout", "limit" })
            if (widget[member] is { } value) body[member] = value.DeepClone();
        if (Str(widget, "view") is { Length: > 0 } viewName)
            body["view_id"] = await ViewIdAsync(Str(widget, "target"), viewName, ct).ConfigureAwait(false);
        await v2.SendAsync(HttpMethod.Patch, $"widgets/{SchemaApiJson.Id(row)}", body, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // ── Options and type display ────────────────────────────────────────────

    private (string PropertyId, string TagId) OptionIds(JsonObject data)
    {
        string property = Str(data, "property");
        string name = Str(data, "name");
        if (!live.PropertyIds.TryGetValue(property, out string? propertyId))
            throw new InvalidOperationException($"Propriété « {property} » introuvable pour son option « {name} ».");
        if (!live.TagIds.TryGetValue(property + "|" + name, out string? tagId))
            throw new InvalidOperationException($"Option « {name} » de « {property} » introuvable.");
        return (propertyId, tagId);
    }

    // v2 writes a colour only when it creates an option; v1 changes one in place,
    // name and colour together (runbook, color-options.ps1, 2026-11-12).
    private async Task RecolourOptionAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        var (propertyId, tagId) = OptionIds(step.Data!);
        await api.UpdatePropertyTagAsync(v2.SpaceId, propertyId, tagId,
            new JsonObject { ["name"] = Str(step.Data, "name"), ["color"] = Str(step.Data, "color") }, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    private async Task DeleteOptionAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        var (propertyId, tagId) = OptionIds(step.Data!);
        await RefusableAsync(step, log, () => api.DeletePropertyTagAsync(v2.SpaceId, propertyId, tagId, ct)).ConfigureAwait(false);
    }

    private async Task SetTypeDisplayAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        string typeId = refs.TypeId(Str(step.Data, "type"));
        await v2.PatchOpsAsync($"objects/{typeId}",
            [new JsonObject { ["op"] = "set_properties", ["set"] = step.Data!["set"]!.DeepClone() }], ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // ── Rewrite and prune ───────────────────────────────────────────────────

    // One edit: every top-level block goes with its subtree, then the plan's body
    // comes in at the end. Applied as a whole or refused as a whole, so a page is
    // never left half written. The page keeps its id.
    private async Task RewritePageAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject page = step.Data!;
        string id = refs.ObjectId(Str(page, "name"));
        JsonObject doc = await v2.GetAsync($"objects/{id}", ct).ConfigureAwait(false);
        var ops = new List<JsonObject>();
        foreach (JsonObject block in (doc["blocks"] as JsonArray ?? []).OfType<JsonObject>())
            if ((block["indent"]?.GetValue<int>() ?? 0) == 0 && Str(block, "id").Length > 0)
                ops.Add(new JsonObject { ["op"] = "delete_block", ["id"] = Str(block, "id"), ["recursive"] = true });
        if (page["blocks"] is JsonArray blocks && blocks.Count > 0)
            ops.Add(new JsonObject { ["op"] = "insert_blocks", ["blocks"] = InsertForm(PlanViews.BodyToWire(blocks, refs)) });
        if (ops.Count > 0) await v2.PatchOpsAsync($"objects/{id}", ops, ct).ConfigureAwait(false);
        await ReplacePlaceholderAsync(id, log, ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // A creation body nests by `indent`; insert_blocks by `relative_indent`, which
    // at the end of a document is the same depth.
    private static JsonArray InsertForm(JsonArray wire)
    {
        foreach (JsonObject block in wire.OfType<JsonObject>())
            if (block["indent"] is { } indent)
            {
                block.Remove("indent");
                block["relative_indent"] = indent;
            }
        return wire;
    }

    private async Task DeleteWidgetAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        string target = Str(step.Data, "target");
        string targetId = target.Contains(':', StringComparison.Ordinal) ? ResolveMember(target) : target;
        JsonObject? row = (await v2.ListAsync("widgets", [new("scope", "space")], ct).ConfigureAwait(false))
            .FirstOrDefault(w => Str(w, "target") == targetId);
        if (row is null) { log.Add($"déjà absent : {step.Detail}"); return; }
        await v2.SendAsync(HttpMethod.Delete, $"widgets/{SchemaApiJson.Id(row)}", null, ct).ConfigureAwait(false);
        log.Add($"supprimé : {step.Detail}");
    }

    private async Task RemoveFromCollectionAsync(PlanStep step, List<string> log, CancellationToken ct)
    {
        JsonObject data = step.Data!;
        var items = new JsonArray();
        foreach (JsonNode? item in data["items"] as JsonArray ?? []) items.Add(ResolveMember(item!.GetValue<string>()));
        await v2.PatchOpsAsync($"objects/{refs.ObjectId(Str(data, "name"))}",
            [new JsonObject { ["op"] = "remove_items", ["items"] = items }], ct).ConfigureAwait(false);
        log.Add(step.Detail);
    }

    // A removal the server refuses (a 403 on an object another key created, a
    // native the app keeps, a 404 already gone) is said and the run goes on: a
    // prune is a list of independent removals, and one refusal blocks none of the
    // others.
    private static async Task RefusableAsync(PlanStep step, List<string> log, Func<Task> remove)
    {
        try
        {
            await remove().ConfigureAwait(false);
            log.Add($"supprimé : {step.Detail}");
        }
        catch (AnytypeV2Exception ex) when (ex.Status is 403 or 404 or 409)
        {
            log.Add($"refusé ({ex.Status}) : {step.Detail} — {ex.ApiMessage ?? ex.Message}");
        }
        catch (HttpRequestException ex) when (ex is not AnytypeV2Exception)
        {
            log.Add($"refusé : {step.Detail} — {ex.Message}");
        }
    }

    private static string Str(JsonObject? node, string key) => node?[key]?.GetValue<string>() ?? "";
}
