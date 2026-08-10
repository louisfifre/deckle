using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

internal static class SchemaPlanner
{
    internal static SchemaPreview Build(
        string spaceAlias,
        string spaceId,
        SchemaManifest manifest,
        SchemaSnapshot snapshot)
    {
        var actions = new List<SchemaAction>();
        var conflicts = new List<string>();
        var skippedConflicts = new List<string>();

        foreach (PropertySpec prop in manifest.Properties)
        {
            if (snapshot.Properties.TryGetValue(prop.Key, out SchemaPropertyInfo? existing))
            {
                if (!string.Equals(existing.Format, prop.Format, StringComparison.Ordinal))
                    conflicts.Add(
                        $"propriété {prop.Key} : format existant {existing.Format}, demandé {prop.Format}");
            }
            else
            {
                actions.Add(new SchemaAction("create_property", prop.Key, prop.Name));
            }

            if (IsTagFormat(prop.Format))
            {
                snapshot.TagsByProperty.TryGetValue(prop.Key, out var tags);
                tags ??= new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
                foreach (TagSpec tag in prop.Tags)
                    if (!HasTag(tags, tag))
                        actions.Add(new SchemaAction("create_tag", $"{prop.Key}:{tag.MatchKey}", tag.Name));
            }
        }

        foreach (TypeSpec type in manifest.Types)
        {
            bool exists = snapshot.Types.TryGetValue(type.Key, out SchemaTypeInfo? existingType);
            if (!exists)
                actions.Add(new SchemaAction("create_type", type.Key, type.Name));

            if (type.Icon is not null)
            {
                if (!exists || existingType!.Icon is null)
                    actions.Add(new SchemaAction("set_icon", type.Key, type.Icon.Display));
                else if (!type.Icon.Matches(existingType.Icon))
                    skippedConflicts.Add(
                        $"set_icon · {type.Key} · icône existante {existingType.Icon.Display}, " +
                        $"demandée {type.Icon.Display}");
            }

            foreach (string propKey in type.Properties)
            {
                if (!manifest.Properties.Any(p => p.Key == propKey) && !snapshot.Properties.ContainsKey(propKey))
                {
                    conflicts.Add($"type {type.Key} : propriété demandée inconnue {propKey}");
                    continue;
                }

                bool alreadyAttached = snapshot.Types.TryGetValue(type.Key, out SchemaTypeInfo? existing)
                    && IsPropertyAttached(existing, snapshot, propKey);
                if (!alreadyAttached)
                    actions.Add(new SchemaAction("attach_property", $"{type.Key}:{propKey}", propKey));
            }
        }

        var preview = new SchemaPreview(
            Id: string.Empty,
            SpaceAlias: spaceAlias,
            SpaceId: spaceId,
            Manifest: manifest,
            Snapshot: snapshot,
            Actions: actions,
            Conflicts: conflicts,
            SkippedConflicts: skippedConflicts);
        return preview with { Id = PreviewId(preview) };
    }

    internal static string Render(SchemaPreview preview)
    {
        var sb = new StringBuilder();
        sb.Append("Preview ").Append(preview.Id)
            .Append(" · espace ").Append(preview.SpaceAlias)
            .Append(" · ").Append(preview.SpaceId).Append('\n');

        if (preview.Conflicts.Count > 0)
        {
            sb.Append("Conflits :\n");
            foreach (string conflict in preview.Conflicts)
                sb.Append("- ").Append(conflict).Append('\n');
        }

        if (preview.SkippedConflicts.Count > 0)
        {
            sb.Append("Conflits ignorés (additif seulement) :\n");
            foreach (string conflict in preview.SkippedConflicts)
                sb.Append("- ").Append(conflict).Append('\n');
        }

        if (preview.Actions.Count == 0)
        {
            sb.Append("Aucune création additive nécessaire.");
            return sb.ToString().TrimEnd();
        }

        sb.Append("Actions additives :\n");
        foreach (SchemaAction action in preview.Actions)
        {
            sb.Append("- ").Append(action.Kind).Append(" · ").Append(action.Key);
            if (action.Kind == "set_icon")
                sb.Append(" · ").Append(action.Name);
            sb.Append('\n');
        }

        sb.Append("Relire puis appeler schema_apply avec confirm:true et preview_id:")
            .Append(preview.Id).Append('.');
        return sb.ToString().TrimEnd();
    }

    internal static bool IsTagFormat(string format) =>
        string.Equals(format, "select", StringComparison.Ordinal)
        || string.Equals(format, "multi_select", StringComparison.Ordinal);

    internal static bool HasTag(IReadOnlyDictionary<string, SchemaTagInfo> tags, TagSpec tag) =>
        tags.ContainsKey(tag.MatchKey)
        || tags.Values.Any(existing =>
            string.Equals(existing.Name, tag.Name, StringComparison.OrdinalIgnoreCase));

    private static bool IsPropertyAttached(SchemaTypeInfo type, SchemaSnapshot snapshot, string propKey)
    {
        return snapshot.Properties.TryGetValue(propKey, out SchemaPropertyInfo? prop)
            ? type.PropertyLinks.Any(link => LinkMatches(link, prop))
            : type.PropertyLinks.Any(link => string.Equals(link.Key, propKey, StringComparison.Ordinal));
    }

    internal static IReadOnlyList<SchemaPropertyLinkInfo> RequestedPropertyLinks(
        TypeSpec type,
        IReadOnlyDictionary<string, SchemaPropertyInfo> propertiesByKey)
    {
        var links = new List<SchemaPropertyLinkInfo>();
        foreach (string propKey in type.Properties)
        {
            if (!propertiesByKey.TryGetValue(propKey, out SchemaPropertyInfo? property))
                throw new InvalidOperationException(
                    $"Propriété « {propKey} » introuvable pour le type « {type.Key} ».");
            if (!links.Any(link => LinkMatches(link, property)))
                links.Add(LinkFrom(property));
        }
        return links;
    }

    internal static JsonObject TypeCreatePayload(TypeSpec spec, IReadOnlyList<SchemaPropertyLinkInfo> links)
    {
        var payload = new JsonObject
        {
            ["key"] = spec.Key,
            ["name"] = spec.Name,
            ["plural_name"] = spec.PluralName,
            ["layout"] = spec.Layout,
        };
        if (links.Count > 0)
            payload["properties"] = PropertyLinkArray(links);
        if (spec.Icon is not null)
            payload["icon"] = spec.Icon.ToPayload();
        return payload;
    }

    internal static IEnumerable<SchemaPropertyLinkInfo> ResolveTypePropertyLinks(
        SchemaTypeInfo type,
        IReadOnlyDictionary<string, SchemaPropertyInfo> propertiesByKey)
    {
        foreach (SchemaPropertyLinkInfo link in type.PropertyLinks)
        {
            if (TryResolveLink(link, propertiesByKey, out SchemaPropertyInfo? property))
            {
                yield return LinkFrom(property!);
                continue;
            }

            if (link.HasPayload)
            {
                yield return link;
                continue;
            }

            throw new InvalidOperationException(
                $"Type « {type.Key} » : lien de propriété existant impossible à résoudre. " +
                "schema_apply refuse de réécrire ce type sans key, name et format.");
        }
    }

    private static bool TryResolveLink(
        SchemaPropertyLinkInfo link,
        IReadOnlyDictionary<string, SchemaPropertyInfo> propertiesByKey,
        out SchemaPropertyInfo? property)
    {
        if (link.Key.Length > 0 && propertiesByKey.TryGetValue(link.Key, out property))
            return true;

        if (link.Id.Length > 0)
        {
            property = propertiesByKey.Values.FirstOrDefault(p =>
                string.Equals(p.Id, link.Id, StringComparison.Ordinal));
            return property is not null;
        }

        property = null;
        return false;
    }

    internal static JsonArray PropertyLinkArray(IEnumerable<SchemaPropertyLinkInfo> links)
    {
        var properties = new JsonArray();
        foreach (SchemaPropertyLinkInfo link in links)
        {
            if (!link.HasPayload)
                throw new InvalidOperationException(
                    "Un lien de propriété Anytype doit porter key, name et format.");

            properties.Add(new JsonObject
            {
                ["key"] = link.Key,
                ["name"] = link.Name,
                ["format"] = link.Format,
            });
        }
        return properties;
    }

    internal static SchemaPropertyLinkInfo LinkFrom(SchemaPropertyInfo property) =>
        new(property.Id, property.Key, property.Name, property.Format);

    internal static bool LinkMatches(SchemaPropertyLinkInfo link, SchemaPropertyInfo property) =>
        (link.Key.Length > 0 && string.Equals(link.Key, property.Key, StringComparison.Ordinal))
        || (link.Id.Length > 0 && string.Equals(link.Id, property.Id, StringComparison.Ordinal));

    internal static string OrDefault(string value, string fallback) =>
        value.Length > 0 ? value : fallback;

    private static string PreviewId(SchemaPreview preview)
    {
        var contract = new StringBuilder();
        Append(contract, preview.SpaceAlias.ToLowerInvariant());
        Append(contract, preview.SpaceId);

        foreach (PropertySpec property in preview.Manifest.Properties)
        {
            Append(contract, "property");
            Append(contract, property.Key);
            Append(contract, property.Name);
            Append(contract, property.Format);
            foreach (TagSpec tag in property.Tags)
            {
                Append(contract, tag.MatchKey);
                Append(contract, tag.Name);
                Append(contract, tag.Key ?? string.Empty);
                Append(contract, tag.Color ?? string.Empty);
            }
        }

        foreach (TypeSpec type in preview.Manifest.Types)
        {
            Append(contract, "type");
            Append(contract, type.Key);
            Append(contract, type.Name);
            Append(contract, type.PluralName);
            Append(contract, type.Layout);
            Append(contract, type.Icon?.Format ?? string.Empty);
            Append(contract, type.Icon?.Name ?? string.Empty);
            Append(contract, type.Icon?.Color ?? string.Empty);
            Append(contract, type.Icon?.Emoji ?? string.Empty);
            foreach (string property in type.Properties)
                Append(contract, property);
        }

        foreach (SchemaAction action in preview.Actions)
        {
            Append(contract, action.Kind);
            Append(contract, action.Key);
            Append(contract, action.Name);
        }
        foreach (string conflict in preview.Conflicts)
            Append(contract, "conflict:" + conflict);
        foreach (string conflict in preview.SkippedConflicts)
            Append(contract, "skipped:" + conflict);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(contract.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Append(StringBuilder target, string value) =>
        target.Append(value.Length).Append(':').Append(value);


}
