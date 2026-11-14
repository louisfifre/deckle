using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Checks a plan against itself, before any call: every reference resolves, every
// property a view names exists, and the order of creation can be followed. A plan
// that passes cannot fail halfway for a reason that was in the file.
//
// Why the properties of a view are checked: Anytype accepts a column, a sort or a
// filter on a property that does not exist and draws nothing for it, until the
// property appears and the column shows up (the « Epic everywhere » of 2026-11-13).
internal static class SpacePlanValidator
{
    private static readonly HashSet<string> ViewTypes =
        new(StringComparer.Ordinal) { "table", "list", "gallery", "kanban", "calendar", "graph" };

    private static readonly HashSet<string> Conditions = new(StringComparer.Ordinal)
    {
        "equal", "not_equal", "greater", "less", "greater_or_equal", "less_or_equal", "contains",
        "not_contains", "in", "not_in", "empty", "not_empty", "all_in", "not_all_in", "exact_in",
        "not_exact_in", "exists",
    };

    public static IReadOnlyList<string> Validate(SpacePlan plan)
    {
        var problems = new List<string>();
        var propertyKeys = new HashSet<string>(plan.Manifest.Properties.Select(p => p.Key), StringComparer.Ordinal);
        var tags = plan.Manifest.Properties
            .Where(p => p.Tags.Count > 0)
            .ToDictionary(p => p.Key, p => p.Tags.Select(t => t.Name).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var typeKeys = new HashSet<string>(plan.Manifest.Types.Select(t => t.Key), StringComparer.Ordinal);
        foreach (string native in SpacePlanReader.SystemTypeKeys.Concat(["project", "task"])) typeKeys.Add(native);

        bool KnownProperty(string key) =>
            propertyKeys.Contains(key) || KnownProperties.IsKnown(key);

        // Names must be unique inside each section.
        Unique(problems, plan.Templates.Select(t => Name(t) + "|" + Str(t, "for")), "templates (nom|for)");
        Unique(problems, plan.Collections.Select(Name), "collections");
        Unique(problems, plan.Queries.Select(Name), "queries");
        Unique(problems, plan.Pages.Select(Name), "pages");

        foreach (TypeSpec type in plan.Manifest.Types)
        {
            plan.TypeExtras.TryGetValue(type.Key, out PlanTypeExtras? extras);
            var own = new HashSet<string>(type.Properties, StringComparer.Ordinal);
            if (extras is not null)
            {
                foreach (string key in extras.Featured.Concat(extras.Hidden))
                    if (!type.Properties.Contains(key, StringComparer.Ordinal))
                        problems.Add($"type {type.Key} : « {key} » est dans « panel » mais pas dans « properties » ; une propriété d'en-tête ou masquée est d'abord une propriété du type.");
                foreach (string key in extras.Featured)
                    if (extras.Hidden.Contains(key))
                        problems.Add($"type {type.Key} : « {key} » est à la fois en en-tête et masquée.");

                if (extras.DefaultTemplate is { } template
                    && !plan.Templates.Any(t => Name(t) == template && Str(t, "for") == type.Key))
                    problems.Add($"type {type.Key} : le modèle par défaut « {template} » n'est pas déclaré dans « templates » pour ce type.");

                if (extras.Views is not null)
                    ValidateViews(problems, $"type {type.Key}", extras.Views, own, KnownProperty, tags, typeKeys);
            }
        }

        foreach (JsonObject template in plan.Templates)
        {
            string label = $"modèle « {Name(template)} »";
            string forKey = Str(template, "for");
            if (Name(template).Length == 0) problems.Add("un modèle n'a pas de « name ».");
            if (!typeKeys.Contains(forKey))
                problems.Add($"{label} : le type « {forKey} » n'est pas déclaré.");
            ValidateBody(problems, label, template["blocks"] as JsonArray, KnownProperty, tags, typeKeys, plan);
        }

        foreach (JsonObject page in plan.Pages)
            ValidateBody(problems, $"page « {Name(page)} »", page["blocks"] as JsonArray, KnownProperty, tags, typeKeys, plan);

        foreach (JsonObject query in plan.Queries)
        {
            string label = $"requête « {Name(query)} »";
            foreach (JsonNode? t in query["source"]?["types"] as JsonArray ?? [])
                if (!typeKeys.Contains(t?.GetValue<string>() ?? ""))
                    problems.Add($"{label} : le type « {t} » n'est pas déclaré.");
            if (query["views"] is JsonArray views)
                ValidateViews(problems, label, views, null, KnownProperty, tags, typeKeys);
        }

        foreach (JsonObject collection in plan.Collections)
        {
            string label = $"collection « {Name(collection)} »";
            foreach (JsonNode? item in collection["items"] as JsonArray ?? [])
            {
                string symbol = item?.GetValue<string>() ?? "";
                if (symbol.StartsWith("type:", StringComparison.Ordinal) && !typeKeys.Contains(symbol[5..]))
                    problems.Add($"{label} : le type « {symbol[5..]} » n'est pas déclaré.");
            }
            if (collection["views"] is JsonArray views)
                ValidateViews(problems, label, views, null, KnownProperty, tags, typeKeys);
        }

        foreach (JsonObject widget in plan.Widgets)
        {
            string target = Str(widget, "target");
            if (target.StartsWith("type:", StringComparison.Ordinal) && !typeKeys.Contains(target[5..]))
                problems.Add($"widget : le type « {target[5..]} » n'est pas déclaré.");
            else if (target.Length == 0)
                problems.Add("un widget n'a pas de « target ».");
        }

        foreach (JsonObject obj in plan.Objects)
        {
            string type = Str(obj, "type");
            if (!typeKeys.Contains(type)) problems.Add($"objet « {Name(obj)} » : le type « {type} » n'est pas déclaré.");
            if (Name(obj).Length == 0) problems.Add("un objet n'a pas de « name ».");
            if (Str(obj, "template") is { Length: > 0 } t and not "none"
                && !plan.Templates.Any(x => Name(x) == t && Str(x, "for") == type))
                problems.Add($"objet « {Name(obj)} » : le modèle « {t} » n'est pas déclaré pour le type « {type} ».");
        }

        return problems;
    }

    private static void ValidateBody(
        List<string> problems, string label, JsonArray? blocks, Func<string, bool> known,
        Dictionary<string, HashSet<string>> tags, HashSet<string> typeKeys, SpacePlan plan)
    {
        foreach (JsonObject block in (blocks ?? []).OfType<JsonObject>())
        {
            if (Str(block, "type") != "dataview") continue;
            string source = Str(block, "source");
            if (!source.StartsWith("type:", StringComparison.Ordinal) || !typeKeys.Contains(source[5..]))
            {
                if (!source.StartsWith("object:", StringComparison.Ordinal))
                    problems.Add($"{label} : la source « {source} » d'une vue en corps doit être « type:<clé> » ou « object:<nom> ».");
                else if (!plan.Collections.Concat(plan.Queries).Any(c => Name(c) == source["object:".Length..]))
                    problems.Add($"{label} : l'objet « {source["object:".Length..]} » n'est ni une collection ni une requête du plan.");
                continue;
            }
            string typeKey = source[5..];
            var own = plan.Manifest.Types.FirstOrDefault(t => t.Key == typeKey) is { } spec
                ? new HashSet<string>(spec.Properties, StringComparer.Ordinal)
                : null;
            if (own is not null && plan.TypeExtras.TryGetValue(typeKey, out PlanTypeExtras? extras))
                foreach (string key in extras.Featured.Concat(extras.Hidden)) own.Add(key);
            ValidateViews(problems, label + " · " + source, block["views"] as JsonArray ?? [], own, known, tags, typeKeys);
        }
    }

    // `own` is the type's own properties when the view sits over one type: a
    // property outside it and outside the known ones is reported.
    private static void ValidateViews(
        List<string> problems, string label, JsonArray views, HashSet<string>? own,
        Func<string, bool> known, Dictionary<string, HashSet<string>> tags, HashSet<string> typeKeys)
    {
        Unique(problems, views.OfType<JsonObject>().Select(Name), $"{label} · vues");
        foreach (JsonObject view in views.OfType<JsonObject>())
        {
            string viewLabel = $"{label} · vue « {Name(view)} »";
            if (Name(view).Length == 0) problems.Add($"{label} : une vue n'a pas de « name ».");
            if (Str(view, "type") is { Length: > 0 } vt && !ViewTypes.Contains(vt))
                problems.Add($"{viewLabel} : type de vue « {vt} » inconnu ({string.Join(", ", ViewTypes)}).");

            bool Resolves(string property) => known(property) || (own?.Contains(property) ?? false);
            void Check(string role, string property)
            {
                if (property.Length == 0) problems.Add($"{viewLabel} : un {role} n'a pas de « property ».");
                else if (!Resolves(property))
                    problems.Add($"{viewLabel} : le {role} nomme « {property} », propriété ni déclarée ni connue.");
            }

            if (Str(view, "group_by") is { Length: > 0 } group) Check("regroupement", group);
            var sorts = view["sorts"] as JsonArray ?? [];
            if (sorts.Count > PlanViews.MaxSorts) problems.Add($"{viewLabel} : {sorts.Count} tris, l'API en prend {PlanViews.MaxSorts} au plus.");
            foreach (JsonObject sort in sorts.OfType<JsonObject>()) Check("tri", Str(sort, "property"));
            var filters = view["filters"] as JsonArray ?? [];
            if (filters.Count > PlanViews.MaxFilters) problems.Add($"{viewLabel} : {filters.Count} filtres, l'API en prend {PlanViews.MaxFilters} au plus.");
            foreach (JsonObject filter in filters.OfType<JsonObject>()) CheckFilter(filter);
            foreach (JsonObject column in (view["columns"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string property = Str(column, "property");
                if (PlanViews.IsUnreachableColumn(property))
                    problems.Add($"{viewLabel} : la colonne « {property} » ne peut ni se masquer ni se retirer par l'API ; ne la déclare pas.");
                else Check("colonne", property);
            }
            Unique(problems, (view["columns"] as JsonArray ?? []).OfType<JsonObject>().Select(c => Str(c, "property")), $"{viewLabel} · colonnes");

            void CheckFilter(JsonObject filter)
            {
                if (filter["filters"] is JsonArray inner)
                {
                    foreach (JsonObject child in inner.OfType<JsonObject>()) CheckFilter(child);
                    return;
                }
                string property = Str(filter, "property");
                Check("filtre", property);
                if (Str(filter, "condition") is { Length: > 0 } cond && !Conditions.Contains(cond))
                    problems.Add($"{viewLabel} : condition de filtre « {cond} » inconnue.");
                foreach (string name in OptionNames(filter["value"]))
                    if (!tags.TryGetValue(property, out var options) || !options.Contains(name))
                        problems.Add($"{viewLabel} : l'option « {name} » n'existe pas pour la propriété « {property} » dans le plan.");
            }
        }
    }

    private static IEnumerable<string> OptionNames(JsonNode? value)
    {
        if (value is JsonArray array)
        {
            foreach (JsonNode? item in array)
                foreach (string name in OptionNames(item)) yield return name;
        }
        else if (value is JsonValue scalar && scalar.TryGetValue<string>(out string? text)
            && text.StartsWith(PlanViews.OptionPrefix, StringComparison.Ordinal))
            yield return text[PlanViews.OptionPrefix.Length..];
    }

    private static void Unique(List<string> problems, IEnumerable<string> keys, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            if (!seen.Add(key)) problems.Add($"doublon dans {label} : « {key} ».");
    }

    private static string Name(JsonObject node) => node["name"]?.GetValue<string>() ?? "";
    private static string Str(JsonObject node, string key) => node[key]?.GetValue<string>() ?? "";
}
