using System.Net.Http;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// Reads a source space through the raw client and writes what it holds as a
// replay sheet on the manifest keys. It reads only: nothing in the source
// moves, and no gesture runs.
//
// The old-key bridge (design note 2026-11-09, interpretation I8) is applied
// here and only here, so the replay and the comparison speak the manifest
// alone. Renames are mechanical; what has no place on the new model (dead
// keys, a point category outside the 22, files, links to objects that are not
// exported) is kept raw under `unmapped` and counted, never guessed.
internal sealed class HomeSpaceExporter(AnytypeApiClient api)
{
    private const int PageLimit = 100;

    // Old type key → manifest type key.
    internal static readonly IReadOnlyDictionary<string, string> TypeRenames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["errand"] = HomeSchema.Types.Product,
            ["floor"] = HomeSchema.Types.Zone,
        };

    // Old property key → manifest property key. `floor` → `zone` is not in
    // I8's list but follows from floor → zone on the type: a room that
    // pointed at its floor points at the same object, now a zone.
    internal static readonly IReadOnlyDictionary<string, string> PropertyRenames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["documents"] = HomeSchema.Properties.Manual,
            ["errand_category"] = HomeSchema.Properties.ProductCategory,
            ["memory_size_gb"] = HomeSchema.Properties.MemorySize,
            ["memory_frequency_mhz"] = HomeSchema.Properties.MemoryFrequency,
            ["switch_kind"] = HomeSchema.Properties.SwitchNature,
            ["floor"] = HomeSchema.Properties.Zone,
        };

    // Keys the new model dropped on purpose: kept raw, counted, not mapped.
    internal static readonly IReadOnlySet<string> DeadKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "power_watts", "condition", "light_nature", "existence", "aisle", "survey_date",
    };

    internal const string NotesKey = "notes";
    private const string DoneKey = "done";

    // Anytype's own bookkeeping on every object: not Home information, not
    // reported. Anything else the bridge does not know lands in unmapped,
    // so a key missing here shows up in the counts instead of vanishing.
    private static readonly IReadOnlySet<string> SystemKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "links", "backlinks", "mentions", "creator", "created_date", "last_modified_by",
        "last_modified_date", "last_opened_date", "added_date", "last_used_date",
    };

    public async Task<HomeReplaySheet> ExportAsync(
        string spaceId, string? sourceAlias, DateTimeOffset exportedAt, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spaceId);
        var sheet = new HomeReplaySheet
        {
            SourceSpace = spaceId,
            SourceAlias = sourceAlias,
            ExportedAt = exportedAt,
        };
        HomeReplaySheetReport report = sheet.Report;

        HomeObjectIndex index = await HomeObjectIndex.LoadAsync(api, spaceId, ct).ConfigureAwait(false);

        // First pass: which objects travel, under which type, with which
        // body. The selectors relations use need the bodies (an idea is named
        // by its first line), so properties wait for the second pass.
        var exported = new Dictionary<string, (JsonObject Source, HomeReplayObject Sheet)>(StringComparer.Ordinal);
        var collections = new List<JsonObject>();
        foreach (JsonObject value in index.Objects)
        {
            string sourceType = HomeObjectJson.TypeKey(value);
            string type = BridgeType(sourceType);
            if (HomeObjectJson.Layout(value) == "collection" || value["type"]?["layout"]?.GetValue<string>() == "collection")
                collections.Add(value);
            if (!HomeSchema.CreatableTypes.Contains(type, StringComparer.Ordinal))
            {
                HomeReplaySheetReport.Increment(report.SkippedTypes, sourceType.Length > 0 ? sourceType : "(sans type)");
                continue;
            }

            string id = HomeObjectJson.Id(value);
            if (id.Length == 0) continue;
            string body = await BodyAsync(spaceId, value, report, ct).ConfigureAwait(false);
            var target = new HomeReplayObject
            {
                OldId = id,
                Type = type,
                Name = type == HomeSchema.Types.Idea ? "" : HomeObjectJson.String(value, "name").Trim(),
                Body = body.Trim().Length > 0 ? body.Trim() : null,
            };
            if (HomeSchema.CodedTypes.Contains(type, StringComparer.Ordinal))
            {
                string code = HomeObjectJson.Code(value);
                if (code.Length > 0) target.Code = code;
            }
            exported[id] = (value, target);
        }

        // Collection memberships: a section-like collection keeps its members
        // by name; a zone that was a collection (the old floor) gives its
        // rooms their zone relation.
        var memberOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var zonesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (JsonObject collection in collections)
        {
            IReadOnlyList<string>? members = await ReadMembersAsync(
                api, spaceId, HomeObjectJson.Id(collection), ct).ConfigureAwait(false);
            if (members is null)
            {
                report.CollectionsUnread++;
                continue;
            }
            string collectionId = HomeObjectJson.Id(collection);
            bool isZone = exported.TryGetValue(collectionId, out var zone) && zone.Sheet.Type == HomeSchema.Types.Zone;
            foreach (string member in members)
            {
                // A zone's rooms get the relation; anything else it held keeps
                // a membership by name, which the replay counts when the
                // target has no such collection.
                bool asZone = isZone && exported.TryGetValue(member, out var held)
                    && held.Sheet.Type == HomeSchema.Types.Room;
                Dictionary<string, List<string>> target = asZone ? zonesOf : memberOf;
                if (!target.TryGetValue(member, out List<string>? list)) target[member] = list = [];
                list.Add(asZone ? collectionId : HomeObjectJson.String(collection, "name").Trim());
            }
        }

        foreach ((JsonObject source, HomeReplayObject target) in exported.Values)
        {
            BridgeProperties(source, target, exported, report);

            if (target.Type == HomeSchema.Types.Room
                && !target.Properties.ContainsKey(HomeSchema.Properties.Zone)
                && zonesOf.TryGetValue(target.OldId, out List<string>? zoneIds))
            {
                string[] zones = zoneIds.Distinct(StringComparer.Ordinal).ToArray();
                target.Properties[HomeSchema.Properties.Zone] =
                    new JsonArray(zones.Select(zone => (JsonNode?)exported[zone].Sheet.Selector).ToArray());
                target.References[HomeSchema.Properties.Zone] = [.. zones];
            }

            if (memberOf.TryGetValue(target.OldId, out List<string>? names))
                target.Collections = names.Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal).ToList();

            HomeReplaySheetReport.Increment(report.Types, target.Type);
            sheet.Objects.Add(target);
        }

        return sheet;
    }

    internal static string BridgeType(string sourceType) =>
        TypeRenames.TryGetValue(sourceType, out string? renamed) ? renamed : sourceType;

    // A listing serves no body for most objects; a detailed read does.
    private async Task<string> BodyAsync(
        string spaceId, JsonObject value, HomeReplaySheetReport report, CancellationToken ct)
    {
        if (value["markdown"] is JsonValue) return HomeObjectJson.String(value, "markdown");
        JsonObject detailed = HomeObjectJson.Unwrap(
            await api.GetObjectAsync(spaceId, HomeObjectJson.Id(value), ct).ConfigureAwait(false));
        report.BodiesRead++;
        return HomeObjectJson.String(detailed, "markdown");
    }

    private static void BridgeProperties(
        JsonObject source,
        HomeReplayObject target,
        IReadOnlyDictionary<string, (JsonObject Source, HomeReplayObject Sheet)> exported,
        HomeReplaySheetReport report)
    {
        if (source["properties"] is not JsonArray properties) return;
        bool isPoint = target.Type == HomeSchema.Types.Point;

        foreach (JsonObject entry in properties.OfType<JsonObject>())
        {
            string key = HomeObjectJson.String(entry, "key");
            if (key.Length == 0 || SystemKeys.Contains(key)) continue;

            if (key == NotesKey)
            {
                string notes = HomeObjectJson.String(entry, "text");
                if (notes.Trim().Length == 0) continue;
                target.Body = AppendNotes(target.Body, notes);
                report.NotesMoved++;
                continue;
            }

            if (key == DoneKey)
            {
                bool done = entry["checkbox"] is JsonValue box && box.TryGetValue(out bool flag) && flag;
                if (target.Type == HomeSchema.Types.Todo) target.Done = done;
                else if (done) Unmap(target, report, key, entry);
                continue;
            }

            if (DeadKeys.Contains(key))
            {
                if (!IsEmpty(entry)) Unmap(target, report, key, entry);
                continue;
            }

            string canonical = PropertyRenames.TryGetValue(key, out string? renamed) ? renamed : key;

            // The code travels on the object itself; a point's room and
            // category are read from its code by the verb that recreates it.
            if (canonical == HomeSchema.Properties.Code && target.Code is not null) continue;
            if (isPoint && canonical == HomeSchema.Properties.InstalledIn) continue;
            if (isPoint && canonical == HomeSchema.Properties.Category)
            {
                if (CategoryCode(entry) is null && !IsEmpty(entry)) Unmap(target, report, key, entry);
                continue;
            }

            string format = HomeObjectJson.String(entry, "format");
            if (!HomeSchema.IsAllowedProperty(target.Type, canonical) || format == "files")
            {
                if (!IsEmpty(entry)) Unmap(target, report, key, entry);
                continue;
            }

            if (entry["objects"] is JsonArray ids)
            {
                BridgeRelation(target, report, key, canonical, ids, exported);
                continue;
            }

            JsonNode? value = PlainValue(entry);
            if (value is not null) target.Properties[canonical] = value;
        }
    }

    // A relation names its targets by selector and keeps their source ids.
    // A target that does not travel (a page, a built-in) or whose type the
    // manifest refuses for this relation stays raw in unmapped.
    private static void BridgeRelation(
        HomeReplayObject target,
        HomeReplaySheetReport report,
        string key,
        string canonical,
        JsonArray ids,
        IReadOnlyDictionary<string, (JsonObject Source, HomeReplayObject Sheet)> exported)
    {
        HomeSchema.ObjectPropertyTargets.TryGetValue(canonical, out IReadOnlyList<string>? allowed);
        var selectors = new JsonArray();
        var references = new List<string>();
        var unresolved = new JsonArray();
        foreach (JsonNode? node in ids)
        {
            if (node is not JsonValue item || !item.TryGetValue(out string? id) || id is null) continue;
            if (exported.TryGetValue(id, out var linked)
                && (allowed is null || allowed.Contains(linked.Sheet.Type, StringComparer.Ordinal)))
            {
                selectors.Add(linked.Sheet.Selector);
                references.Add(id);
            }
            else
            {
                unresolved.Add(id);
            }
        }

        if (references.Count > 0)
        {
            target.Properties[canonical] = selectors;
            target.References[canonical] = references;
        }
        if (unresolved.Count > 0)
        {
            target.Unmapped[key] = unresolved;
            HomeReplaySheetReport.Increment(report.Unmapped, key);
        }
    }

    // The body first, then the old notes after a blank line, under no
    // heading: the body is where free text lives on the new model.
    internal static string AppendNotes(string? body, string notes)
    {
        string trimmedNotes = notes.Trim();
        if (string.IsNullOrWhiteSpace(body)) return trimmedNotes;
        return body.TrimEnd() + "\n\n" + trimmedNotes;
    }

    // The category code an option names ("LC — plafonnier" → LC), or null
    // when it names none of the 22. Read from the option name: the live
    // option keys are hash-derived and say nothing.
    internal static string? CategoryCode(JsonObject entry)
    {
        JsonNode? select = entry["select"];
        string label = select switch
        {
            JsonObject option => HomeObjectJson.String(option, "name") is { Length: > 0 } name
                ? name
                : HomeObjectJson.String(option, "key"),
            JsonValue scalar when scalar.TryGetValue(out string? text) => text ?? "",
            _ => "",
        };
        string code = new string(label.Trim().TakeWhile(char.IsAsciiLetter).ToArray()).ToUpperInvariant();
        return HomeCategories.All.Contains(code) ? code : null;
    }

    // The value as the typed verb takes it: a select by its option name,
    // a multi-select as an array of names, scalars as they are. Null for an
    // empty value, which the sheet leaves out.
    internal static JsonNode? PlainValue(JsonObject entry)
    {
        if (entry["select"] is JsonNode select)
            return OptionName(select) is { Length: > 0 } name ? JsonValue.Create(name) : null;
        if (entry["multi_select"] is JsonArray multi)
        {
            var names = new JsonArray();
            foreach (JsonNode? option in multi)
                if (option is not null && OptionName(option) is { Length: > 0 } name) names.Add(name);
            return names.Count > 0 ? names : null;
        }
        if (entry["number"] is JsonValue number) return number.DeepClone();
        if (entry["checkbox"] is JsonValue checkbox) return checkbox.DeepClone();
        foreach (string key in new[] { "date", "text", "url", "email", "phone" })
            if (entry[key] is JsonValue scalar && scalar.TryGetValue(out string? text)
                && !string.IsNullOrWhiteSpace(text))
                return JsonValue.Create(key == "text" ? text : text.Trim());
        return null;
    }

    private static string OptionName(JsonNode option) => option switch
    {
        JsonObject value => HomeObjectJson.String(value, "name") is { Length: > 0 } name
            ? name.Trim()
            : HomeObjectJson.String(value, "key"),
        JsonValue scalar when scalar.TryGetValue(out string? text) => text?.Trim() ?? "",
        _ => "",
    };

    private static bool IsEmpty(JsonObject entry)
    {
        JsonNode? raw = RawValue(entry);
        return raw switch
        {
            null => true,
            JsonArray array => array.Count == 0,
            JsonValue value when value.TryGetValue(out string? text) => string.IsNullOrWhiteSpace(text),
            _ => false,
        };
    }

    private static void Unmap(HomeReplayObject target, HomeReplaySheetReport report, string key, JsonObject entry)
    {
        target.Unmapped[key] = RawValue(entry)?.DeepClone();
        HomeReplaySheetReport.Increment(report.Unmapped, key);
    }

    // The value node of an entry, under its format's key; the whole entry
    // when the format is unknown, so nothing is dropped.
    private static JsonNode? RawValue(JsonObject entry)
    {
        string format = HomeObjectJson.String(entry, "format");
        if (format.Length > 0 && entry.ContainsKey(format)) return entry[format];
        foreach (string key in new[] { "text", "number", "checkbox", "date", "select", "multi_select", "objects", "files", "url", "email", "phone" })
            if (entry.ContainsKey(key)) return entry[key];
        return entry;
    }

    // Every member of a collection, read through all its views (a filtered
    // view hides members). Null when the collection cannot be read: the
    // caller counts it rather than failing the whole run.
    internal static async Task<IReadOnlyList<string>?> ReadMembersAsync(
        AnytypeApiClient api, string spaceId, string collectionId, CancellationToken ct)
    {
        if (collectionId.Length == 0) return null;
        try
        {
            var members = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<JsonObject> views = await ReadAllAsync(
                offset => api.ListCollectionViewsAsync(spaceId, collectionId, offset, PageLimit, ct)).ConfigureAwait(false);
            foreach (JsonObject view in views)
            {
                string viewId = HomeObjectJson.String(view, "id");
                if (viewId.Length == 0) continue;
                IReadOnlyList<JsonObject> rows = await ReadAllAsync(
                    offset => api.ListCollectionObjectsAsync(spaceId, collectionId, viewId, offset, PageLimit, ct))
                    .ConfigureAwait(false);
                foreach (JsonObject row in rows)
                {
                    string id = HomeObjectJson.Id(HomeObjectJson.Unwrap(row));
                    if (id.Length > 0 && seen.Add(id)) members.Add(id);
                }
            }
            return members;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Advances by the rows served and stops on an empty page, as the other
    // paged reads of the module do.
    private static async Task<IReadOnlyList<JsonObject>> ReadAllAsync(Func<int, Task<JsonObject>> page)
    {
        var result = new List<JsonObject>();
        int offset = 0;
        while (true)
        {
            JsonObject root = await page(offset).ConfigureAwait(false);
            int returned = 0;
            if (root["data"] is JsonArray data)
                foreach (JsonNode? node in data)
                {
                    returned++;
                    if (node is JsonObject value) result.Add(value);
                }
            if (root["pagination"]?["has_more"]?.GetValue<bool>() != true || returned == 0) break;
            offset += returned;
        }
        return result;
    }
}
