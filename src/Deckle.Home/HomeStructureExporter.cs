using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// Reads the structure of a source space: the settings and views of each Home
// type, the views of each section collection, each template whole and the
// widgets in order. It reads only (GET), through the v2 API, and writes what
// it found as the source speaks it: the remap onto the new keys and ids is a
// later, pure step.
//
// Anytype's built-in types (page, collection, file…) are not carried: their
// views are the defaults every space has, and nothing of Home hangs on them.
// A template for such a type, and a widget in the person's personal sidebar,
// are counted as skipped, not exported.
//
// A read the source refuses costs that item only: it is counted under
// `unread_*` and the export goes on.
internal sealed class HomeStructureExporter(AnytypeApiClient api)
{
    public async Task<HomeStructure> ExportAsync(string spaceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        var structure = new HomeStructure();
        var v2 = new HomeV2(api, spaceId);

        await ExportTypesAsync(v2, structure, ct).ConfigureAwait(false);
        await ExportCollectionsAsync(v2, spaceId, structure, ct).ConfigureAwait(false);
        await ExportTemplatesAsync(v2, structure, ct).ConfigureAwait(false);
        await ExportWidgetsAsync(v2, structure, ct).ConfigureAwait(false);
        return structure;
    }

    private async Task ExportTypesAsync(HomeV2 v2, HomeStructure structure, CancellationToken ct)
    {
        Dictionary<string, string> typeIds;
        try { typeIds = await v2.TypeIdsAsync(ct).ConfigureAwait(false); }
        catch (HttpRequestException)
        {
            structure.Count("unread_type_list");
            return;
        }

        // Two source types may bridge to one key (a floor and a zone): the one
        // that already has the manifest key wins, the other is counted.
        IEnumerable<IGrouping<string, string>> byBridgedKey = typeIds.Keys
            .GroupBy(HomeSpaceExporter.BridgeType, StringComparer.Ordinal);
        foreach (IGrouping<string, string> group in byBridgedKey)
        {
            if (!HomeSchema.CreatableTypes.Contains(group.Key, StringComparer.Ordinal))
            {
                structure.Count("types_skipped", group.Count());
                continue;
            }
            string sourceKey = group.Contains(group.Key, StringComparer.Ordinal) ? group.Key : group.First();
            structure.Count("types_duplicate", group.Count() - 1);

            JsonObject? document;
            try { document = await v2.GetAsync($"types/{sourceKey}", ct).ConfigureAwait(false) as JsonObject; }
            catch (HttpRequestException)
            {
                structure.Count("unread_types");
                continue;
            }
            if (document is null)
            {
                structure.Count("unread_types");
                continue;
            }

            JsonObject settings = document["type_settings"] as JsonObject ?? [];
            var type = new HomeStructureType
            {
                Key = sourceKey,
                TypeId = typeIds[sourceKey],
                DefaultView = NonEmpty(settings, "default_view"),
                DefaultTemplate = NonEmpty(settings, "default_template"),
            };
            foreach (JsonObject definition in (settings["property_definitions"] as JsonArray ?? []).OfType<JsonObject>())
                if (NonEmpty(definition, "property") is { } property)
                    type.Properties.Add(new HomeStructureProperty
                    {
                        Property = property,
                        Section = NonEmpty(definition, "section"),
                    });
            if (HomeV2.DataviewBlock(document)?["views"] is JsonArray views)
                type.Views = (JsonArray)views.DeepClone();

            structure.Types.Add(type);
            structure.Count("types");
            structure.Count("type_views", type.Views.Count);
        }
    }

    // The section collections: collection-layout objects that are not
    // themselves a Home object (the old floor collections become zones, which
    // are ordinary objects with a template, not a collection).
    private async Task ExportCollectionsAsync(HomeV2 v2, string spaceId, HomeStructure structure, CancellationToken ct)
    {
        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(api, spaceId, ct).ConfigureAwait(false);
        foreach (JsonObject value in index.Objects)
        {
            bool isCollection = HomeObjectJson.Layout(value) == "collection"
                || value["type"]?["layout"]?.GetValue<string>() == "collection";
            if (!isCollection) continue;
            if (HomeSchema.CreatableTypes.Contains(
                    HomeSpaceExporter.BridgeType(HomeObjectJson.TypeKey(value)), StringComparer.Ordinal))
                continue;
            string id = HomeObjectJson.Id(value);
            if (id.Length == 0) continue;

            try
            {
                IReadOnlyList<JsonObject> views = await v2.ListAsync($"collections/{id}/views", ct)
                    .ConfigureAwait(false);
                structure.Collections.Add(new HomeStructureCollection
                {
                    OldId = id,
                    Name = HomeObjectJson.String(value, "name").Trim(),
                    Views = new JsonArray(views.Select(view => (JsonNode?)view).ToArray()),
                });
                structure.Count("collections");
                structure.Count("collection_views", views.Count);
            }
            catch (HttpRequestException)
            {
                structure.Count("unread_collections");
            }
        }
    }

    private static async Task ExportTemplatesAsync(HomeV2 v2, HomeStructure structure, CancellationToken ct)
    {
        IReadOnlyList<JsonObject> rows;
        try { rows = await v2.ListAsync("templates", ct).ConfigureAwait(false); }
        catch (HttpRequestException)
        {
            structure.Count("unread_template_list");
            return;
        }

        foreach (JsonObject row in rows)
        {
            string id = HomeObjectJson.Id(row);
            if (id.Length == 0) continue;
            JsonObject? document;
            try { document = await v2.GetAsync($"objects/{id}", ct).ConfigureAwait(false) as JsonObject; }
            catch (HttpRequestException)
            {
                structure.Count("unread_templates");
                continue;
            }
            if (document is null)
            {
                structure.Count("unread_templates");
                continue;
            }

            string templateFor = HomeObjectJson.String(document, "template_for");
            if (templateFor.StartsWith("type-", StringComparison.Ordinal)) templateFor = templateFor["type-".Length..];
            if (!HomeSchema.CreatableTypes.Contains(HomeSpaceExporter.BridgeType(templateFor), StringComparer.Ordinal))
            {
                structure.Count("templates_skipped");
                continue;
            }

            structure.Templates.Add(new HomeStructureTemplate
            {
                OldId = id,
                Name = HomeObjectJson.String(document["properties"] as JsonObject ?? [], "name").Trim(),
                TemplateFor = templateFor,
                Document = document,
            });
            structure.Count("templates");
            structure.Count("template_blocks", (document["blocks"] as JsonArray)?.Count ?? 0);
        }
    }

    // The widgets of the sidebar every member sees, in the order served. A
    // widget of the person's personal sidebar is theirs, not the space's.
    private static async Task ExportWidgetsAsync(HomeV2 v2, HomeStructure structure, CancellationToken ct)
    {
        IReadOnlyList<JsonObject> rows;
        try { rows = await v2.ListAsync("widgets", ct).ConfigureAwait(false); }
        catch (HttpRequestException)
        {
            structure.Count("unread_widgets");
            return;
        }

        foreach (JsonObject row in rows)
        {
            if (HomeObjectJson.String(row, "scope") is { Length: > 0 } scope && scope != "space")
            {
                structure.Count("widgets_skipped");
                continue;
            }
            string target = HomeObjectJson.String(row, "target");
            if (target.Length == 0) continue;
            structure.Widgets.Add(new HomeStructureWidget
            {
                OldId = HomeObjectJson.Id(row),
                Target = target,
                Layout = NonEmpty(row, "layout"),
                Limit = row["limit"] is JsonValue limit && limit.TryGetValue(out int count) ? count : null,
                ViewId = NonEmpty(row, "view_id"),
            });
            structure.Count("widgets");
        }
    }

    private static string? NonEmpty(JsonObject value, string key) =>
        HomeObjectJson.String(value, key) is { Length: > 0 } text ? text : null;
}
