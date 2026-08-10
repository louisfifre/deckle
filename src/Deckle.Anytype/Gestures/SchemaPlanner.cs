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
        SchemaSnapshot snapshot,
        IReadOnlyList<SchemaCollectionObjectInfo> collectionObjects)
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

        // Sections: reuse an existing built-in collection object bearing the
        // exact section name, create it otherwise. Membership is unreadable on
        // this surface, so every listed type is planned as an additive add; the
        // list endpoint unions members, a re-add neither fails nor duplicates.
        var sectionCollections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SectionSpec section in manifest.Sections)
        {
            var matches = collectionObjects
                .Where(c => string.Equals(c.Name, section.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count > 1)
                conflicts.Add(
                    $"section {section.Name} : plusieurs collections portent déjà ce nom — " +
                    "renomme les doublons dans Anytype puis relance schema_preview.");
            else if (matches.Count == 1)
                sectionCollections[section.Name] = matches[0].Id;
            else
                actions.Add(new SchemaAction("create_section", section.Name, section.Name));

            foreach (string typeKey in section.Types)
            {
                if (!manifest.Types.Any(t => t.Key == typeKey) && !snapshot.Types.ContainsKey(typeKey))
                {
                    conflicts.Add(
                        $"section {section.Name} : type demandé inconnu {typeKey} — " +
                        "déclare-le dans « types » ou crée-le dans l'espace avant de provisionner la section.");
                    continue;
                }
                actions.Add(new SchemaAction("add_to_section", $"{section.Name}:{typeKey}", typeKey));
            }
        }

        return new SchemaPreview(
            Id: PreviewId(),
            SpaceAlias: spaceAlias,
            SpaceId: spaceId,
            Manifest: manifest,
            Snapshot: snapshot,
            Actions: actions,
            Conflicts: conflicts,
            SkippedConflicts: skippedConflicts,
            SectionCollections: sectionCollections);
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

        if (preview.Manifest.Sections.Count > 0)
        {
            sb.Append("Sections :\n");
            foreach (SectionSpec section in preview.Manifest.Sections)
                sb.Append("- ").Append(section.Name).Append(" · ")
                    .Append(preview.SectionCollections.ContainsKey(section.Name)
                        ? "réutilisation de la collection existante"
                        : "création")
                    .Append('\n');
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

    internal static void EnsureNoUnpreviewedActions(SchemaPreview preview, SchemaPreview livePlan)
    {
        var previewed = preview.Actions
            .Select(a => $"{a.Kind}:{a.Key}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (SchemaAction action in livePlan.Actions)
            if (!previewed.Contains($"{action.Kind}:{action.Key}"))
                throw new InvalidOperationException(
                    "L'état Anytype a changé depuis le preview. Relance schema_preview avant schema_apply.");
    }

    private static string PreviewId()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }


}
