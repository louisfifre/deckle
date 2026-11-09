using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Home;

// The recreation rehearsal end to end, so the maintenance test that runs it
// stays a thin caller: export the source into a sheet, put the manifest on
// the TEST space, replay the sheet through the typed gestures, read back and
// compare, write the counts beside the sheet.
//
// It refuses before reading anything when the target is not a test space by
// construction: the alias `home` (the live space), the alias `dev` (the
// project-management space of the credentials), an alias resolving to the
// source's own id, or a target that already holds Home objects (a second
// run would double every object; a rehearsal starts on an empty space).
//
// The export runs before the schema step, not after: the manifest applied on
// the target is the compiled one plus the option names the sheet uses in its
// open and seeded vocabularies (a brand, a supplier, a botanical family the
// person added in the app). Without them, every object carrying such an
// option would be refused, and the rehearsal would measure the missing
// options instead of the recreation. Closed vocabularies stay as compiled.
internal sealed class HomeRecreationRehearsal(AnytypeApiClient api, AnytypeSpaceAliases aliases)
{
    public const string LiveAlias = "home";
    public const string DevAlias = "dev";

    public async Task<HomeRehearsalResult> RunAsync(
        string sourceAlias, string targetAlias, string sheetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        string sourceId = aliases.Resolve(sourceAlias);
        string targetId = aliases.Resolve(targetAlias);
        RefuseUnsafeTarget(sourceId, targetAlias, targetId);

        HomeObjectIndex existing = await HomeObjectIndex.LoadAsync(api, targetId, ct).ConfigureAwait(false);
        int occupied = existing.Objects.Count(value =>
            HomeSchema.CreatableTypes.Contains(HomeObjectJson.TypeKey(value), StringComparer.Ordinal));
        if (occupied > 0)
            throw new InvalidOperationException(
                $"L'espace cible « {targetAlias} » contient déjà {occupied} objet(s) Home : une répétition part "
                + "d'un espace vide, sinon chaque objet serait doublé. Crée un nouvel espace de test.");

        // (b) Export, written at once so a later failure leaves the sheet.
        HomeReplaySheet sheet = await new HomeSpaceExporter(api)
            .ExportAsync(sourceId, sourceAlias.Trim(), DateTimeOffset.Now, ct).ConfigureAwait(false);
        sheet.Save(sheetPath);

        // (a) The manifest on the target, through the schema-admin gestures.
        (JsonObject manifest, int addedOptions) = ManifestFor(sheet);
        var admin = new SchemaAdminGestures(api, aliases);
        SchemaPreviewResult preview = await admin.PreviewResultAsync(targetAlias, manifest, ct).ConfigureAwait(false);
        if (preview.Conflicts.Count > 0)
            throw new InvalidOperationException(
                $"Le manifeste Home ne s'applique pas sur « {targetAlias} » : "
                + string.Join(" ; ", preview.Conflicts) + ". La feuille est écrite ; rien n'a été recréé.");
        if (preview.Actions.Count > 0)
            await admin.ApplyAsync(targetAlias, preview.PreviewId, manifest, confirm: true, ct).ConfigureAwait(false);

        // (c) Replay, (d) compare.
        HomeReplayReport replay = await new HomeSpaceReplayer(api, targetId)
            .ReplayAsync(sheet, HomeReplaySheet.Beside(sheetPath, "replay"), ct).ConfigureAwait(false);
        HomeComparison comparison = await new HomeRecreationComparer(api)
            .CompareAsync(sheet, targetId, HomeReplaySheet.Beside(sheetPath, "compare"), ct).ConfigureAwait(false);

        // (e) The counts beside the sheet.
        var result = new HomeRehearsalResult(
            sheetPath, sheet.Report, preview.Actions.Count, addedOptions, replay, comparison);
        HomeReplaySheet.WriteJson(HomeReplaySheet.Beside(sheetPath, "rehearsal"), result.Counts());
        return result;
    }

    internal static void RefuseUnsafeTarget(string sourceId, string targetAlias, string targetId)
    {
        string alias = targetAlias.Trim();
        if (string.Equals(alias, LiveAlias, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Refusé : la cible d'une répétition n'est jamais l'espace Home vivant (alias « home »). "
                + "Donne l'alias d'un espace de TEST.");
        if (string.Equals(alias, DevAlias, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Refusé : l'alias « dev » est l'espace de gestion de projet, pas un espace de test.");
        if (string.Equals(sourceId.Trim(), targetId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusé : la source et la cible désignent le même espace. La cible doit être un espace de TEST distinct.");
    }

    // The compiled manifest plus, for each open or seeded select of the
    // sheet, the option names no manifest option answers to. In memory only:
    // the names are the person's and reach nothing but the test space.
    internal static (JsonObject Manifest, int AddedOptions) ManifestFor(HomeReplaySheet sheet)
    {
        JsonObject manifest = HomeSchema.TargetManifest;
        Dictionary<string, JsonObject> properties = manifest["properties"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(property => property["key"]!.GetValue<string>(), StringComparer.Ordinal);
        int added = 0;

        foreach (HomeReplayObject value in sheet.Objects)
            foreach ((string key, JsonNode? node) in value.Properties)
            {
                if (node is null || !properties.TryGetValue(key, out JsonObject? property)) continue;
                string format = property["format"]?.GetValue<string>() ?? "";
                if (format is not ("select" or "multi_select") || HomeSchema.ClosedVocabularies.ContainsKey(key))
                    continue;

                // A plain array, never a JsonArray: wrapping the sheet's node
                // in a JsonArray would re-parent it (see HomePropertyWriter).
                IEnumerable<JsonNode?> names = node is JsonArray array ? array : new JsonNode?[] { node };
                foreach (JsonNode? item in names)
                {
                    if (item is not JsonValue scalar || !scalar.TryGetValue(out string? name)) continue;
                    name = name?.Trim();
                    if (string.IsNullOrEmpty(name) || Answers(key, property, name)) continue;
                    if (property["tags"] is not JsonArray tags) property["tags"] = tags = [];
                    tags.Add(name);
                    added++;
                }
            }
        return (manifest, added);
    }

    private static bool Answers(string propertyKey, JsonObject property, string name)
    {
        if (property["tags"] is not JsonArray tags) return false;
        foreach (JsonNode? tag in tags)
        {
            if (tag is JsonValue plain && plain.TryGetValue(out string? text)
                && string.Equals(text?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (tag is JsonObject option && option["key"]?.GetValue<string>() is { } key
                && (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(option["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase)
                    || HomeSchema.OptionAliases(propertyKey, key).Contains(name, StringComparer.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}

// What a rehearsal measured, counts only: safe to print in the test output.
internal sealed record HomeRehearsalResult(
    string SheetPath,
    HomeReplaySheetReport Export,
    int SchemaActions,
    int AddedOptions,
    HomeReplayReport Replay,
    HomeComparison Comparison)
{
    public string Summary()
    {
        static string Counts(IReadOnlyDictionary<string, int> counts) =>
            counts.Count == 0 ? "aucun" : string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"));

        var text = new StringBuilder();
        text.AppendLine($"Feuille : {SheetPath}");
        text.AppendLine($"Export : {Export.Exported} objet(s) ({Counts(Export.Types)}).");
        text.AppendLine($"  Types ignorés : {Counts(Export.SkippedTypes)}.");
        text.AppendLine($"  Valeurs non reprises : {Counts(Export.Unmapped)}.");
        text.AppendLine($"  Notes versées au corps : {Export.NotesMoved} ; corps lus : {Export.BodiesRead} ; "
            + $"collections illisibles : {Export.CollectionsUnread}.");
        text.AppendLine($"Schéma : {SchemaActions} action(s) appliquée(s), dont {AddedOptions} option(s) reprise(s) de la feuille.");
        text.AppendLine($"Rejeu : {Replay.Created} créé(s) sur {Replay.SheetObjects} ({Counts(Replay.CreatedByType)}).");
        text.AppendLine($"  Refus : {Replay.Refused} ({Counts(Replay.RefusedByReason)}).");
        text.AppendLine($"  Relations posées après coup : {Replay.RelationUpdates} ; relations perdues : {Replay.RelationsDropped} ; "
            + $"tâches terminées : {Replay.CompletedTodos}.");
        text.AppendLine($"  Appartenances perdues : {Replay.CollectionsDropped} ; créés depuis un modèle : {Replay.Templated} ; "
            + $"créés sans id relu : {Replay.CreatedWithoutId}.");
        text.AppendLine($"Comparaison : {Comparison.Equal} égal(aux), {Comparison.Different} différent(s), "
            + $"{Comparison.Missing} manquant(s), {Comparison.Extra} en trop.");
        text.Append($"  Valeurs : {Comparison.ValuesCompared} comparée(s), {Comparison.ValuesDifferent} différente(s) "
            + $"({Counts(Comparison.DifferentByField)})"
            + (Comparison.CollectionsUnread ? " ; appartenances non relues." : "."));
        return text.ToString();
    }

    public JsonObject Counts()
    {
        static JsonObject Map(IReadOnlyDictionary<string, int> counts)
        {
            var result = new JsonObject();
            foreach ((string key, int value) in counts) result[key] = value;
            return result;
        }

        return new JsonObject
        {
            ["export"] = new JsonObject
            {
                ["objects"] = Export.Exported,
                ["types"] = Map(Export.Types),
                ["skipped_types"] = Map(Export.SkippedTypes),
                ["unmapped"] = Map(Export.Unmapped),
                ["notes_moved"] = Export.NotesMoved,
                ["bodies_read"] = Export.BodiesRead,
                ["collections_unread"] = Export.CollectionsUnread,
            },
            ["schema"] = new JsonObject { ["actions"] = SchemaActions, ["added_options"] = AddedOptions },
            ["replay"] = new JsonObject
            {
                ["sheet_objects"] = Replay.SheetObjects,
                ["created"] = Map(Replay.CreatedByType),
                ["refused"] = Map(Replay.RefusedByReason),
                ["relation_updates"] = Replay.RelationUpdates,
                ["relations_dropped"] = Replay.RelationsDropped,
                ["completed_todos"] = Replay.CompletedTodos,
                ["collections_dropped"] = Replay.CollectionsDropped,
                ["templated"] = Replay.Templated,
                ["created_without_id"] = Replay.CreatedWithoutId,
            },
            ["comparison"] = new JsonObject
            {
                ["equal"] = Comparison.Equal,
                ["different"] = Comparison.Different,
                ["missing"] = Comparison.Missing,
                ["extra"] = Comparison.Extra,
                ["values_compared"] = Comparison.ValuesCompared,
                ["values_different"] = Comparison.ValuesDifferent,
                ["different_by_field"] = Map(Comparison.DifferentByField),
                ["collections_unread"] = Comparison.CollectionsUnread,
            },
        };
    }
}
