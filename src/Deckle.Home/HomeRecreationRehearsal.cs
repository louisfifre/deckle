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
        await RefuseOccupiedTargetAsync(targetAlias, targetId, ct).ConfigureAwait(false);

        // (b) Export, written at once so a later failure leaves the sheet.
        HomeReplaySheet sheet = await ExportSheetAsync(sourceId, sourceAlias, sheetPath, ct).ConfigureAwait(false);
        return await RecreateAsync(sheet, targetAlias, targetId, sheetPath, ct).ConfigureAwait(false);
    }

    // The export alone: the sheet on disk, nothing written anywhere else. The
    // first half of a run split across two networks, when the bot that reads
    // the source cannot reach the target (a cut-over from the official
    // network to a self-hosted one): export now, replay the file later.
    public async Task<HomeReplaySheetReport> ExportAsync(string sourceAlias, string sheetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        string sourceId = aliases.Resolve(sourceAlias);
        HomeReplaySheet sheet = await ExportSheetAsync(sourceId, sourceAlias, sheetPath, ct).ConfigureAwait(false);
        return sheet.Report;
    }

    // The second half: a sheet exported earlier, replayed on an empty test
    // space. The source may live on another network, so its id is unknown
    // here; the target is refused when its alias is the one the sheet was
    // exported from, on top of the usual refusals.
    public async Task<HomeRehearsalResult> ReplayFromSheetAsync(
        string sheetPath, string targetAlias, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        HomeReplaySheet sheet = HomeReplaySheet.Load(sheetPath);
        if (string.Equals(sheet.SourceAlias?.Trim(), targetAlias.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Refusé : la feuille a été exportée de « {sheet.SourceAlias} », la cible ne peut pas être ce même alias.");
        string targetId = aliases.Resolve(targetAlias);
        RefuseUnsafeTarget("", targetAlias, targetId);
        await RefuseOccupiedTargetAsync(targetAlias, targetId, ct).ConfigureAwait(false);
        return await RecreateAsync(sheet, targetAlias, targetId, sheetPath, ct).ConfigureAwait(false);
    }

    // The rebirth itself: the sheet replayed on the live Home, the one target
    // every rehearsal refuses. Two locks replace the test-space ones. The
    // caller repeats the live space's id, so the alias `home` alone never
    // opens the way: an alias still pointing at the old network's space, or
    // edited by mistake, is caught before anything is read. And the space must
    // hold no object at all, not only no Home object: a live space is replayed
    // once, on the day it is born empty, and a second run is refused rather
    // than doubling every object.
    public async Task<HomeRehearsalResult> ReplayOnLiveHomeAsync(
        string sheetPath, string confirmedSpaceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetPath);
        HomeReplaySheet sheet = HomeReplaySheet.Load(sheetPath);
        string targetId = aliases.Resolve(LiveAlias);
        RefuseUnconfirmedLiveTarget(sheet.SourceSpace, targetId, confirmedSpaceId);

        HomeObjectIndex existing = await HomeObjectIndex.LoadAsync(api, targetId, ct).ConfigureAwait(false);
        if (existing.Objects.Count > 0)
            throw new InvalidOperationException(
                $"Refusé : l'espace Home vivant contient déjà {existing.Objects.Count} objet(s). Le rejeu ne part "
                + "que d'un espace vide : un second passage doublerait chaque objet.");
        return await RecreateAsync(sheet, LiveAlias, targetId, sheetPath, ct).ConfigureAwait(false);
    }

    internal static void RefuseUnconfirmedLiveTarget(string sourceSpace, string targetId, string confirmedSpaceId)
    {
        if (string.IsNullOrWhiteSpace(confirmedSpaceId)
            || !string.Equals(confirmedSpaceId.Trim(), targetId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusé : l'id confirmé ne correspond pas à l'espace que l'alias « home » désigne. "
                + "Vérifie spaces.json, puis redonne l'id exact de l'espace Home vivant.");
        if (string.Equals(sourceSpace.Trim(), targetId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusé : la feuille a été exportée de cet espace même ; elle ne s'y rejoue pas.");
    }

    private async Task<HomeReplaySheet> ExportSheetAsync(
        string sourceId, string sourceAlias, string sheetPath, CancellationToken ct)
    {
        HomeReplaySheet sheet = await new HomeSpaceExporter(api)
            .ExportAsync(sourceId, sourceAlias.Trim(), DateTimeOffset.Now, ct).ConfigureAwait(false);

        // The structure (type settings and views, section-collection views,
        // templates, widgets), read after the objects and saved with them.
        sheet.Structure = await new HomeStructureExporter(api).ExportAsync(sourceId, ct).ConfigureAwait(false);
        sheet.Version = HomeReplaySheet.StructureVersion;
        sheet.Save(sheetPath);
        return sheet;
    }

    private async Task RefuseOccupiedTargetAsync(string targetAlias, string targetId, CancellationToken ct)
    {
        HomeObjectIndex existing = await HomeObjectIndex.LoadAsync(api, targetId, ct).ConfigureAwait(false);
        int occupied = existing.Objects.Count(value =>
            HomeSchema.CreatableTypes.Contains(HomeObjectJson.TypeKey(value), StringComparer.Ordinal));
        if (occupied > 0)
            throw new InvalidOperationException(
                $"L'espace cible « {targetAlias} » contient déjà {occupied} objet(s) Home : une répétition part "
                + "d'un espace vide, sinon chaque objet serait doublé. Crée un nouvel espace de test.");
    }

    // Schema, replay, compare and counts, from a sheet already on disk.
    private async Task<HomeRehearsalResult> RecreateAsync(
        HomeReplaySheet sheet, string targetAlias, string targetId, string sheetPath, CancellationToken ct)
    {
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

        // (e) The structure, after the objects it names exist: write on the
        // target, read back, compare. A sheet exported before the structure was
        // carried has none.
        HomeStructureResult? structure = sheet.Structure is null
            ? null
            : await RecreateStructureAsync(sheet.Structure, replay, targetId, sheetPath, ct).ConfigureAwait(false);

        // (f) The counts beside the sheet.
        var result = new HomeRehearsalResult(
            sheetPath, sheet.Report, preview.Actions.Count, addedOptions, replay, comparison, structure);
        HomeReplaySheet.WriteJson(HomeReplaySheet.Beside(sheetPath, "rehearsal"), result.Counts());
        return result;
    }

    // A refusal of the API is counted inside the writer. What is not a refusal
    // (an answer nobody planned for) is kept as the step's failure, so the
    // counts of the objects, already measured, are still written.
    internal async Task<HomeStructureResult> RecreateStructureAsync(
        HomeStructure structure, HomeReplayReport replay, string targetId, string sheetPath, CancellationToken ct)
    {
        try
        {
            HomeStructureOutcome outcome = await new HomeStructureWriter(api, targetId)
                .WriteAsync(structure, replay.CreatedIds, HomeReplaySheet.Beside(sheetPath, "structure"), ct)
                .ConfigureAwait(false);
            HomeStructureComparison comparison = await new HomeStructureComparer(api)
                .CompareAsync(outcome.Plan, targetId, HomeReplaySheet.Beside(sheetPath, "structure-compare"), ct)
                .ConfigureAwait(false);
            return new HomeStructureResult(structure.Report, outcome.Report, comparison, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HomeReplaySheet.WriteJson(
                HomeReplaySheet.Beside(sheetPath, "structure-error"), new { type = ex.GetType().Name, message = ex.Message });
            return new HomeStructureResult(structure.Report, null, null, ex.GetType().Name);
        }
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
        if (sourceId.Trim().Length > 0 && string.Equals(sourceId.Trim(), targetId.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusé : la source et la cible désignent le même espace. La cible doit être un espace de TEST distinct.");
    }

    // The compiled manifest plus, for each open or seeded select of the
    // sheet, the option names no manifest option answers to. In memory only:
    // the names are the person's and reach nothing but the test space.
    internal static (JsonObject Manifest, int AddedOptions) ManifestFor(HomeReplaySheet sheet)
    {
        JsonObject manifest = Localized(HomeSchema.TargetManifest);
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

    // The manifest copied from the Home repository names its types, properties
    // and options in English; the space shows the person's words, which live in
    // the terms file (HomeTerms). Without this overlay a recreated space comes
    // back in English while the space it replaces spoke French (found on the
    // live Home on 2026-11-12: the keys were right, every label was English).
    // A key the terms file does not name throws: a missing word is a gap in the
    // terms file, not something to guess.
    internal static JsonObject Localized(JsonObject manifest)
    {
        HomeTerms terms = HomeTerms.Current;
        foreach (JsonObject type in manifest["types"]!.AsArray().OfType<JsonObject>())
        {
            string key = type["key"]!.GetValue<string>();
            type["name"] = terms.TypeName(key);
            type["plural_name"] = terms.TypePluralName(key);
        }
        foreach (JsonObject property in manifest["properties"]!.AsArray().OfType<JsonObject>())
        {
            string key = property["key"]!.GetValue<string>();
            property["name"] = terms.PropertyName(key);
            if (property["tags"] is not JsonArray tags) continue;
            foreach (JsonObject option in tags.OfType<JsonObject>())
                option["name"] = terms.OptionName(key, option["key"]!.GetValue<string>());
        }
        return manifest;
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
    HomeComparison Comparison,
    HomeStructureResult? Structure = null)
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
        text.AppendLine();
        text.Append(StructureSummary());
        return text.ToString();
    }

    private string StructureSummary()
    {
        static string Counts(IReadOnlyDictionary<string, int> counts) =>
            counts.Count == 0 ? "aucun" : string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"));

        if (Structure is null)
            return "Structure : absente de la feuille (exportée avant son report), rien à recréer.";
        var text = new StringBuilder();
        text.AppendLine($"Structure, export : {Counts(Structure.Exported)}.");
        if (Structure.Failure is not null)
            return text.Append($"Structure, écriture interrompue : {Structure.Failure} (détail dans le fichier structure-error).")
                .ToString();

        HomeStructureWriteReport write = Structure.Write!;
        HomeStructureComparison read = Structure.Comparison!;
        text.AppendLine($"Structure, écriture : {Counts(write.Written)}.");
        text.AppendLine($"  Refus : {Counts(write.Refused)} ; introuvables sur la cible : {Counts(write.Missing)}.");
        text.AppendLine($"  Non repris (propriété ou id sans place sur le nouveau modèle) : {Counts(write.Dropped)}.");
        text.Append($"Structure, relecture : {read.Compared} fait(s) comparé(s), {read.Different} différent(s) "
            + $"({Counts(read.DifferentByField)}) ; illisibles : {Counts(read.Unread)}.");
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
            ["structure"] = StructureCounts(),
        };
    }

    private JsonNode StructureCounts()
    {
        static JsonObject Map(IReadOnlyDictionary<string, int> counts)
        {
            var result = new JsonObject();
            foreach ((string key, int value) in counts) result[key] = value;
            return result;
        }

        if (Structure is null) return new JsonObject { ["carried"] = false };
        var counts = new JsonObject { ["carried"] = true, ["export"] = Map(Structure.Exported) };
        if (Structure.Failure is not null)
        {
            counts["failure"] = Structure.Failure;
            return counts;
        }
        counts["written"] = Map(Structure.Write!.Written);
        counts["refused"] = Map(Structure.Write.Refused);
        counts["missing"] = Map(Structure.Write.Missing);
        counts["dropped"] = Map(Structure.Write.Dropped);
        counts["comparison"] = new JsonObject
        {
            ["compared"] = Structure.Comparison!.Compared,
            ["different"] = Structure.Comparison.Different,
            ["different_by_field"] = Map(Structure.Comparison.DifferentByField),
            ["unread"] = Map(Structure.Comparison.Unread),
        };
        return counts;
    }
}

// What the structure step measured, counts only. Failure is the type name of
// an answer nobody planned for; the write and the comparison are then absent.
internal sealed record HomeStructureResult(
    IReadOnlyDictionary<string, int> Exported,
    HomeStructureWriteReport? Write,
    HomeStructureComparison? Comparison,
    string? Failure);
