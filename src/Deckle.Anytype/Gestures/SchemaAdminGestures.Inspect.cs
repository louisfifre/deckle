using System.Text;
using Deckle.Diagnostics;

namespace Deckle.Anytype;

// schema_inspect_space: everything a space plan needs to recreate a space by
// hand or by manifest, in reading order — types, properties with the options
// of each select, the views of each type, then the views of each set and
// collection — and, last, what the API does not render, so the plan knows
// what stays manual. Two API versions meet here on purpose: v1 is the only one
// that names an option by key and id, v2 the only one that serves views.
public sealed partial class SchemaAdminGestures
{
    public async Task<string> InspectAsync(string space, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        string spaceId = aliases.Resolve(space);

        SchemaSnapshot snapshot = await _snapshotReader.BuildAsync(spaceId, null, ct);

        var options = new Dictionary<string, IReadOnlyList<SchemaTagInfo>>(StringComparer.Ordinal);
        foreach (SchemaPropertyInfo prop in snapshot.Properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
            if (SchemaPlanner.IsTagFormat(prop.Format) && prop.Id.Length > 0)
                options[prop.Key] = await _snapshotReader.ReadTagListAsync(spaceId, prop.Id, ct);

        // A key v2 refuses (one minted before anytype-cli 0.5.0 serves v1 only)
        // would refuse every type alike: the first refusal stops the view reads.
        var viewReader = new SchemaViewReader(api.V2);
        var typeViews = new List<(SchemaTypeInfo Type, SchemaViewsRead Views)>();
        string? denied = null;
        foreach (SchemaTypeInfo type in snapshot.Types.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            SchemaViewsRead read = await viewReader.ReadTypeViewsAsync(spaceId, type.Key, ct);
            if (read.Denied)
            {
                denied = read.Failure;
                break;
            }
            typeViews.Add((type, read));
        }
        SchemaSetsRead? sets = denied is null ? await viewReader.ReadSetsAsync(spaceId, ct) : null;

        string text = RenderInspection(space, spaceId, snapshot, options, typeViews, sets, denied);
        DeckleAnytypeSource.Log.GestureCompleted("schema_inspect_space", Elapsed(started));
        return text;
    }

    private static string RenderInspection(
        string space,
        string spaceId,
        SchemaSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<SchemaTagInfo>> options,
        IReadOnlyList<(SchemaTypeInfo Type, SchemaViewsRead Views)> typeViews,
        SchemaSetsRead? sets,
        string? denied)
    {
        var sb = new StringBuilder();
        sb.Append("Espace ").Append(space).Append(" : ").Append(spaceId).Append('\n');
        sb.Append("Types : ").Append(snapshot.Types.Count).Append('\n');
        foreach (SchemaTypeInfo type in snapshot.Types.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
            sb.Append("- ").Append(type.Key).Append(" · ").Append(type.Name).Append(" · ").Append(type.Layout)
                .Append(" · ").Append(type.Icon?.Display ?? "icon:none").Append('\n');

        sb.Append("Propriétés : ").Append(snapshot.Properties.Count).Append('\n');
        foreach (SchemaPropertyInfo prop in snapshot.Properties.Values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append("- ").Append(prop.Key).Append(" · ").Append(prop.Name).Append(" · ").Append(prop.Format).Append('\n');
            if (!options.TryGetValue(prop.Key, out IReadOnlyList<SchemaTagInfo>? tags)) continue;
            if (tags.Count == 0) sb.Append("  aucune option\n");
            foreach (SchemaTagInfo tag in tags)
                sb.Append("  - ").Append(tag.Key.Length > 0 ? tag.Key : "(sans clé)")
                    .Append(" · ").Append(tag.Name)
                    .Append(" · ").Append(tag.Color.Length > 0 ? tag.Color : "(sans couleur)").Append('\n');
        }

        var unlisted = new SortedSet<string>(StringComparer.Ordinal);
        sb.Append("Vues par type (la première vue d'un type est son onglet par défaut) :\n");
        if (denied is not null)
            sb.Append("- non lues : l'API v2 refuse cette clé (").Append(denied)
                .Append("). Une clé créée avant anytype-cli 0.5.0 ne sert que v1.\n");
        foreach ((SchemaTypeInfo type, SchemaViewsRead read) in typeViews)
        {
            sb.Append("- ").Append(type.Key).Append(" · ").Append(type.Name).Append(" · ");
            AppendViews(sb, read, snapshot, options, unlisted);
        }

        if (sets is not null)
        {
            sb.Append("Ensembles et collections : ").Append(sets.Sources.Count).Append('\n');
            foreach (SchemaViewSource source in sets.Sources)
            {
                sb.Append("- ").Append(source.Name.Length > 0 ? source.Name : "(sans nom)")
                    .Append(" · ").Append(source.Kind).Append(" · ").Append(source.Id).Append(" · ");
                AppendViews(sb, source.Views, snapshot, options, unlisted);
            }
            if (sets.Failure is not null)
                sb.Append("- recherche interrompue : ").Append(sets.Failure).Append('\n');
        }

        AppendLimits(sb, unlisted);
        return sb.ToString().TrimEnd();
    }

    private static void AppendViews(
        StringBuilder sb,
        SchemaViewsRead read,
        SchemaSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<SchemaTagInfo>> options,
        SortedSet<string> unlisted)
    {
        if (read.Failure is not null)
        {
            sb.Append("vues illisibles : ").Append(read.Failure).Append('\n');
            return;
        }
        if (read.Views.Count == 0)
        {
            sb.Append("aucune vue\n");
            return;
        }

        sb.Append(read.Views.Count).Append(read.Views.Count == 1 ? " vue\n" : " vues\n");
        int position = 0;
        foreach (SchemaViewInfo view in read.Views)
        {
            position++;
            sb.Append("  ").Append(position).Append(". ").Append(view.Name.Length > 0 ? view.Name : "(sans nom)")
                .Append(" · ").Append(view.Layout);
            if (view.GroupBy is not null) sb.Append(" · groupé par ").Append(view.GroupBy);
            sb.Append('\n');

            sb.Append("     filtres : ").Append(view.Filters.Count == 0 ? "aucun" : string.Join(" ; ", view.Filters)).Append('\n');
            sb.Append("     tris : ").Append(view.Sorts.Count == 0 ? "aucun" : string.Join(" ; ", view.Sorts)).Append('\n');

            string[] shown = view.Columns.Where(c => !c.Hidden).Select(c => c.Property).ToArray();
            int hidden = view.Columns.Count - shown.Length;
            sb.Append("     colonnes affichées : ").Append(shown.Length == 0 ? "aucune" : string.Join(", ", shown));
            if (hidden > 0) sb.Append(" · ").Append(hidden).Append(hidden == 1 ? " masquée" : " masquées");
            sb.Append('\n');
            foreach (SchemaViewColumn column in view.Columns)
                if (!snapshot.Properties.ContainsKey(column.Property)) unlisted.Add(column.Property);

            if (view.Groups.Count > 0)
            {
                IReadOnlyList<SchemaTagInfo> groupOptions =
                    view.GroupBy is not null && options.TryGetValue(view.GroupBy, out var tags) ? tags : [];
                sb.Append("     groupes : ")
                    .Append(string.Join(" · ", view.Groups.Select(g => Group(g, groupOptions)))).Append('\n');
            }
            if (view.ManualOrderGroups > 0)
                sb.Append("     ordre manuel des cartes : ").Append(view.ManualOrderGroups)
                    .Append(view.ManualOrderGroups == 1 ? " groupe\n" : " groupes\n");
            if (view.Settings.Count > 0)
                sb.Append("     réglages : ")
                    .Append(string.Join(", ", view.Settings.Select(s => $"{s.Key}={s.Value}"))).Append('\n');
        }
    }

    // A select's groups are named by option id: the option's name stands in
    // for it when the group-by property's options were read.
    private static string Group(SchemaViewGroup group, IReadOnlyList<SchemaTagInfo> options)
    {
        string label = group.Id == "empty"
            ? "(vide)"
            : options.FirstOrDefault(o => o.Id == group.Id)?.Name ?? group.Id;
        if (group.Color is not null) label += " " + group.Color;
        if (group.Hidden) label += " masqué";
        return label;
    }

    private static void AppendLimits(StringBuilder sb, SortedSet<string> unlisted)
    {
        sb.Append("Non rendu par l'API :\n");
        sb.Append("- Options : v2 n'en rend que le nom et la couleur ; la clé et l'id viennent de v1.\n");
        sb.Append("- Ordre des options : celui servi par v1 ; v2 trie par nom ; l'ordre affiché dans Anytype n'est garanti par aucune des deux.\n");
        sb.Append("- Options d'une propriété absente de la liste des propriétés : v1 ne la liste pas, v2 répond 404.\n");
        sb.Append("- Vue active : état local à chaque appareil, jamais servi ; seul l'onglet par défaut (la première vue) est connu.\n");
        if (unlisted.Count > 0)
            sb.Append("Colonnes dont la propriété est absente de la liste : ")
                .Append(string.Join(", ", unlisted)).Append('\n');
        sb.Append("Omis ici : largeur et alignement des colonnes, ordre des colonnes masquées, détail de l'ordre manuel des cartes, id des vues.\n");
        sb.Append("Interprétation : l'API omet les valeurs par défaut ; une vue sans type est lue table, un tri sans sens asc, et les filtres d'une vue se cumulent (et).\n");
    }
}
