using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Deckle.Anytype;

// Which parts of a plan a run covers. The schema and the structure are the
// builder's own. Objects are optional. The sidebar belongs to the space's owner,
// so it is a stage of its own, run with the owner's key.
[Flags]
public enum SpacePlanStages
{
    None = 0,
    Schema = 1,
    Structure = 2,
    Objects = 4,
    Widgets = 8,
    Default = Schema | Structure,
}

public sealed record SpacePlanPreview(
    string PreviewId,
    string SpaceAlias,
    IReadOnlyList<SchemaPreviewAction> Actions,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Notes,
    string Rendered);

// Builds a whole space from one plan, in the right order, and reads one back into
// a plan. It extends the schema admin surface instead of replacing it: the first
// stage is exactly schema_preview / schema_apply on the plan's manifest part; the
// second stage adds what that surface never wrote (panels, views, templates,
// collections, queries, pages, objects, widgets).
//
// Idempotent: a second run on a built space finds nothing to do. A check is a
// preview that stops there and writes nothing.
public sealed class SpacePlanGestures(AnytypeApiClient api, AnytypeSpaceAliases aliases)
{
    private static readonly TimeSpan SearchIndexSettle = TimeSpan.FromSeconds(10);

    private readonly SchemaAdminGestures _schema = new(api, aliases);

    public async Task<SpacePlanPreview> PreviewAsync(
        string space, JsonObject planNode, SpacePlanStages stages, CancellationToken ct = default)
    {
        string spaceId = aliases.Resolve(space);
        SpacePlan plan = SpacePlan.Parse(planNode);
        IReadOnlyList<string> problems = SpacePlanValidator.Validate(plan);
        if (problems.Count > 0)
            return new SpacePlanPreview(string.Empty, space, [], problems, [], [], Render(space, [], problems, [], []));

        var actions = new List<SchemaPreviewAction>();
        var conflicts = new List<string>();
        var notes = new List<string>();
        var digest = new StringBuilder(planNode.ToJsonString());

        if (stages.HasFlag(SpacePlanStages.Schema) && HasSchema(plan))
        {
            SchemaPreviewResult schema = await _schema.PreviewResultAsync(space, plan.ManifestNode, ct).ConfigureAwait(false);
            actions.AddRange(schema.Actions);
            conflicts.AddRange(schema.Conflicts);
            notes.AddRange(schema.SkippedConflicts);
            digest.Append(schema.PreviewId);
        }

        if (stages.HasFlag(SpacePlanStages.Structure) || stages.HasFlag(SpacePlanStages.Objects) || stages.HasFlag(SpacePlanStages.Widgets))
        {
            (IReadOnlyList<PlanStep> steps, _) = await StepsAsync(spaceId, plan, stages, ct).ConfigureAwait(false);
            foreach (PlanStep step in steps)
            {
                actions.Add(new SchemaPreviewAction(step.Kind, step.Key, step.Detail));
                digest.Append(step.Kind).Append('|').Append(step.Key).Append('|').Append(step.Detail).Append('\n');
            }
        }

        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString()))).ToLowerInvariant();
        return new SpacePlanPreview(id, space, actions, problems, conflicts, notes, Render(space, actions, problems, conflicts, notes));
    }

    // A check is a preview nobody applies: it answers whether the space is the
    // plan. Run it again ten minutes after a write: a sort or a filter that was
    // written without an id reads right at once and collapses when the object is
    // loaded again.
    public Task<SpacePlanPreview> CheckAsync(
        string space, JsonObject planNode, SpacePlanStages stages, CancellationToken ct = default) =>
        PreviewAsync(space, planNode, stages, ct);

    public async Task<string> ApplyAsync(
        string space, string previewId, JsonObject planNode, SpacePlanStages stages, bool confirm, CancellationToken ct = default)
    {
        if (!confirm)
            throw new InvalidOperationException("space_plan_apply exige confirm:true avec un preview_id relu juste avant.");

        string spaceId = aliases.Resolve(space);
        SpacePlanPreview preview = await PreviewAsync(space, planNode, stages, ct).ConfigureAwait(false);
        if (preview.Problems.Count > 0)
            throw new InvalidOperationException("Le plan ne passe pas la validation : " + string.Join(" ; ", preview.Problems));
        if (!string.Equals(preview.PreviewId, previewId, StringComparison.Ordinal))
            throw new InvalidOperationException("Le plan ou l'état Anytype a changé depuis le preview. Relance le preview.");
        if (preview.Conflicts.Count > 0)
            throw new InvalidOperationException("Preview avec conflit : apply refusé. " + string.Join(" ; ", preview.Conflicts));

        SpacePlan plan = SpacePlan.Parse(planNode);
        var log = new List<string>();
        try
        {
            if (stages.HasFlag(SpacePlanStages.Schema) && HasSchema(plan))
            {
                SchemaPreviewResult schema = await _schema.PreviewResultAsync(space, plan.ManifestNode, ct).ConfigureAwait(false);
                if (schema.Actions.Count > 0)
                    log.Add((await _schema.ApplyAsync(space, schema.PreviewId, plan.ManifestNode, confirm: true, ct).ConfigureAwait(false))
                        .Replace("\n", " "));
            }

            using IDisposable _ = await api.AcquireWriteScopeAsync("space_plan_apply", spaceId, ct).ConfigureAwait(false);
            (IReadOnlyList<PlanStep> steps, SpacePlanRead live) =
                await StepsAsync(spaceId, plan, stages, ct).ConfigureAwait(false);
            var executor = new SpacePlanExecutor(api, new PlanApi(api.V2, spaceId), live.Refs, live.Templates);
            await executor.RunAsync(steps, log, ct).ConfigureAwait(false);

            // The search index lags a few seconds behind a write (runbook, 2026-11-12), and
            // the reader finds pages, collections and queries through it: wait before
            // anyone reads the space again, or a created page reads as missing.
            if (steps.Any(s => s.Kind is "page_create" or "collection_create" or "query_create" or "object_create"))
                await Task.Delay(SearchIndexSettle, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sb = new StringBuilder("Plan appliqué en partie : une écriture a échoué.\nErreur : ").Append(ex.Message).Append('\n');
            sb.Append(log.Count == 0 ? "Rien n'a été écrit avant l'erreur.\n" : "Écrit avant l'erreur :\n");
            foreach (string line in log) sb.Append("- ").Append(line).Append('\n');
            sb.Append("Relance le preview : il montre ce qui reste à faire.");
            throw new InvalidOperationException(sb.ToString(), ex);
        }

        return log.Count == 0 ? "Plan inchangé : rien à appliquer." : "Plan appliqué :\n" + string.Join("\n", log.Select(l => "- " + l));
    }

    // The reverse command: a space read into the plan that would rebuild it.
    public async Task<JsonObject> ReadAsync(
        string space, IReadOnlyCollection<string>? pageNames = null, CancellationToken ct = default)
    {
        string spaceId = aliases.Resolve(space);
        SpacePlanRead read = await new SpacePlanReader(api).ReadAsync(spaceId, pageNames ?? [], ct).ConfigureAwait(false);
        return read.Plan;
    }

    private static bool HasSchema(SpacePlan plan) =>
        plan.Manifest.Types.Count > 0 || plan.Manifest.Properties.Count > 0 || plan.Manifest.Sections.Count > 0;

    private async Task<(IReadOnlyList<PlanStep> Steps, SpacePlanRead Live)> StepsAsync(
        string spaceId, SpacePlan plan, SpacePlanStages stages, CancellationToken ct)
    {
        var pageNames = plan.Pages.Select(p => p["name"]?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList();
        SpacePlanRead live = await new SpacePlanReader(api).ReadAsync(spaceId, pageNames, ct).ConfigureAwait(false);

        var existingObjects = new HashSet<string>(StringComparer.Ordinal);
        if (stages.HasFlag(SpacePlanStages.Objects))
            foreach (JsonObject obj in plan.Objects)
            {
                string type = obj["type"]?.GetValue<string>() ?? "";
                string name = obj["name"]?.GetValue<string>() ?? "";
                // A type the plan has yet to create holds no object, and a search on it is refused.
                if (!live.TypeIds.ContainsKey(type)) continue;
                IReadOnlyList<AnytypeV2SearchRow> hits = await new AnytypeV2Searcher(api.V2, spaceId)
                    .SearchAllAsync(new AnytypeV2SearchRequest { Type = type, Query = name }, ct).ConfigureAwait(false);
                if (hits.Any(h => h.Name == name)) existingObjects.Add(type + "|" + name);
            }

        IReadOnlyList<PlanStep> steps = SpacePlanPlanner.Plan(
            plan, live.Plan, existingObjects,
            includeObjects: stages.HasFlag(SpacePlanStages.Objects),
            includeWidgets: stages.HasFlag(SpacePlanStages.Widgets));
        if (!stages.HasFlag(SpacePlanStages.Structure))
            steps = steps.Where(s => s.Kind.StartsWith("object_", StringComparison.Ordinal)
                || s.Kind.StartsWith("widget_", StringComparison.Ordinal)).ToList();
        return (steps, live);
    }

    private static string Render(
        string space, IReadOnlyList<SchemaPreviewAction> actions, IReadOnlyList<string> problems,
        IReadOnlyList<string> conflicts, IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder("Plan · espace ").Append(space).Append('\n');
        void Section(string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            if (list.Count == 0) return;
            sb.Append(title).Append(" :\n");
            foreach (string line in list) sb.Append("- ").Append(line).Append('\n');
        }
        Section("Plan invalide", problems);
        Section("Conflits", conflicts);
        Section("Laissé tel quel", notes);
        if (problems.Count == 0)
            sb.Append(actions.Count == 0
                ? "Rien à faire : l'espace correspond au plan."
                : $"{actions.Count} action(s) :\n" + string.Join("\n", actions.Select(a => $"- {a.Kind} · {a.Key}" + (a.Name.Length > 0 && a.Name != a.Key ? $" · {a.Name}" : ""))));
        return sb.ToString().TrimEnd();
    }
}
