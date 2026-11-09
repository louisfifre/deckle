using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The v6 project-management manifest applied to a real, EMPTY Anytype space,
// then read back and compared to the manifest. It is the proof the unit tests
// cannot give: that the server accepts a declared option key and keeps it, and
// that layouts, formats and per-type property order land as declared.
//
// An explicit maintenance test, like the Home recreation rehearsal: the
// endpoint, its key and the space id come from variables, the machine's own
// credentials and spaces.json are not read, and nothing is saved. It prints
// counts and the mismatch list only. Run it with
// /p:VSTestCLIRunSettings=xUnit.Explicit=only against a throwaway space.
[Trait("Category", "maintenance")]
public sealed class PmSchemaTrialTests
{
    public const string ApiUrlVariable = "DECKLE_PM_TRIAL_API_URL";
    public const string ApiKeyVariable = "DECKLE_PM_TRIAL_API_KEY";
    public const string SpaceIdVariable = "DECKLE_PM_TRIAL_SPACE_ID";
    public const string ApiVersionVariable = "DECKLE_PM_TRIAL_API_VERSION";

    const string TrialAlias = "pm-trial";
    const string ResourceName = "Deckle.Anytype.Schema.pm-schema-manifest.json";

    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string? Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { } value && value.Trim().Length > 0 ? value.Trim() : null;

    static void Write(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    // Mismatches the trial explains instead of failing on. Empty until a run
    // shows one; each entry is a prefix of a mismatch line and carries its why.
    //
    // A fresh space is not bare: Anytype ships status, priority, done and
    // due_date as properties and project and task as types. The planner renames
    // them to the manifest's names; the manifest speaks the native priority keys
    // p_0..p_5. What remains is what the API cannot change.
    static readonly (string Prefix, string Why)[] Classified =
    [
        ("option-extra status", "native To Do / In Progress / Done options stay beside the declared ones; the planner never deletes an option"),
        ("option-order","the listing order is the server's, not the creation order: the API has no order field on an option"),
    ];

    [Fact(Explicit = true)]
    public async Task ApplyManifestOnExplicitEndpoint()
    {
        string? url = Variable(ApiUrlVariable);
        string? key = Variable(ApiKeyVariable);
        string? spaceId = Variable(SpaceIdVariable);
        Assert.SkipWhen(url is null || key is null || spaceId is null,
            $"Essai non lancé : définis {ApiUrlVariable} (par exemple http://127.0.0.1:41012), "
            + $"{ApiKeyVariable} (clé du bot de test) et {SpaceIdVariable} (id d'un espace de TEST vide).");

        var credentials = new AnytypeCredentials(url!, Variable(ApiVersionVariable) ?? "2025-11-08", key!, spaceId!);
        var aliases = new AnytypeSpaceAliases(new Dictionary<string, string> { [TrialAlias] = spaceId! });
        using var api = new AnytypeApiClient(credentials);
        var gestures = new SchemaAdminGestures(api, aliases);

        using Stream stream = typeof(SchemaManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        JsonObject node = JsonNode.Parse(stream)!.AsObject();
        SchemaManifest manifest = SchemaManifest.Parse(node);

        Write($"Manifeste : {manifest.Types.Count} types, {manifest.Properties.Count} propriétés, "
            + $"{manifest.Properties.Sum(p => p.Tags.Count)} options, {manifest.Sections.Count} sections.");

        SchemaPreviewResult preview = await gestures.PreviewResultAsync(TrialAlias, node, Ct);
        Write($"Preview : {preview.Actions.Count} action(s), {preview.Conflicts.Count} conflit(s), "
            + $"{preview.SkippedConflicts.Count} conflit(s) ignoré(s).");
        foreach (var group in preview.Actions.GroupBy(a => a.Kind))
            Write($"  {group.Key} : {group.Count()}");
        foreach (string conflict in preview.Conflicts) Write("  CONFLIT " + conflict);
        Assert.Empty(preview.Conflicts);

        string outcome = await gestures.ApplyAsync(TrialAlias, preview.PreviewId, node, confirm: true, Ct);
        Write($"Apply : {outcome.Split('\n').Length - 1} ligne(s) de résultat.");

        // A second preview on the applied space: the plan must be empty.
        SchemaPreviewResult again = await gestures.PreviewResultAsync(TrialAlias, node, Ct);
        Write($"Second preview : {again.Actions.Count} action(s) restante(s).");
        foreach (SchemaPreviewAction action in again.Actions) Write($"  reste {action.Kind} {action.Key}");

        var mismatches = await CompareAsync(api, spaceId!, manifest);

        Write($"Écarts : {mismatches.Count}.");
        foreach (string line in mismatches) Write("  " + line);
        // The listing the server serves for the hand-checked properties, so a
        // reading of order and keys needs no second run.
        Write("Options servies (clé/nom, dans l'ordre du serveur) :");
        var reader = new SchemaSnapshotReader(api);
        SchemaSnapshot liveSnapshot = await reader.ReadAsync(spaceId!, null, Ct);
        foreach (PropertySpec p in manifest.Properties.Where(p => p.Tags.Count > 0))
            Write($"  {p.Key}: " + string.Join(", ",
                (await reader.ReadTagListAsync(spaceId!, liveSnapshot.Properties[p.Key].Id, Ct)).Select(o => $"{o.Key}/{o.Name}")));

        var unexplained = mismatches
            .Where(line => !Classified.Any(c => line.StartsWith(c.Prefix, StringComparison.Ordinal)))
            .ToList();
        Assert.True(unexplained.Count == 0, $"{unexplained.Count} écart(s) non expliqué(s).");
        Assert.Empty(again.Actions);
    }

    static async Task<List<string>> CompareAsync(AnytypeApiClient api, string spaceId, SchemaManifest manifest)
    {
        var reader = new SchemaSnapshotReader(api);
        SchemaSnapshot live = await reader.ReadAsync(
            spaceId,
            manifest.Properties.Where(p => SchemaPlanner.IsTagFormat(p.Format)).Select(p => p.Key).ToArray(),
            Ct);

        var mismatches = new List<string>();
        int typesOk = 0, propsOk = 0, optionsOk = 0, optionKeysOk = 0, memberships = 0;

        foreach (PropertySpec spec in manifest.Properties)
        {
            if (!live.Properties.TryGetValue(spec.Key, out SchemaPropertyInfo? property))
            {
                mismatches.Add($"property {spec.Key}: missing");
                continue;
            }
            bool ok = true;
            if (property.Name != spec.Name)
            {
                mismatches.Add($"property {spec.Key}: name « {property.Name} », manifest « {spec.Name} »");
                ok = false;
            }
            if (property.Format != spec.Format)
            {
                mismatches.Add($"property {spec.Key}: format {property.Format}, manifest {spec.Format}");
                ok = false;
            }
            if (ok) propsOk++;

            if (spec.Tags.Count == 0 || !SchemaPlanner.IsTagFormat(spec.Format)) continue;

            IReadOnlyList<SchemaTagInfo> options = await reader.ReadTagListAsync(spaceId, property.Id, Ct);
            // An option is found by its declared key (by name when the manifest
            // declares none): its position in a listing is the server's, not
            // the creation order, so order is judged apart below.
            foreach (TagSpec tag in spec.Tags)
            {
                SchemaTagInfo? got = options.FirstOrDefault(o => tag.Key is not null ? o.Key == tag.Key : o.Name == tag.Name);
                if (got is null)
                {
                    mismatches.Add($"option-key {spec.Key} « {tag.Name} »: no live option with key « {tag.Key} »"
                        + (options.FirstOrDefault(o => o.Name == tag.Name) is { } byName
                            ? $" (the option named so carries key « {byName.Key} »)" : ""));
                    continue;
                }
                if (tag.Key is not null) optionKeysOk++;
                if (got.Name != tag.Name)
                {
                    mismatches.Add($"option-name {spec.Key}:{tag.Key}: name « {got.Name} », manifest « {tag.Name} »");
                    continue;
                }
                optionsOk++;
            }

            string[] liveKeys = options.Select(o => o.Key).ToArray();
            string[] declaredKeys = spec.Tags.Select(t => t.Key ?? t.Name).ToArray();
            string[] liveDeclaredOrder = liveKeys.Where(declaredKeys.Contains).ToArray();
            if (!liveDeclaredOrder.SequenceEqual(declaredKeys.Where(liveDeclaredOrder.Contains)))
                mismatches.Add($"option-order {spec.Key}: live [{string.Join(",", liveDeclaredOrder)}], manifest [{string.Join(",", declaredKeys)}]");
            string[] extras = options.Where(o => !declaredKeys.Contains(o.Key) && !declaredKeys.Contains(o.Name)).Select(o => $"{o.Key}/{o.Name}").ToArray();
            if (extras.Length > 0)
                mismatches.Add($"option-extra {spec.Key}: {string.Join(", ", extras)}");
        }

        foreach (TypeSpec spec in manifest.Types)
        {
            if (!live.Types.TryGetValue(spec.Key, out SchemaTypeInfo? type))
            {
                mismatches.Add($"type {spec.Key}: missing");
                continue;
            }
            bool ok = true;
            if (type.Name != spec.Name)
            {
                mismatches.Add($"type {spec.Key}: name « {type.Name} », manifest « {spec.Name} »");
                ok = false;
            }
            if (type.Layout != spec.Layout)
            {
                mismatches.Add($"type {spec.Key}: layout {type.Layout}, manifest {spec.Layout}");
                ok = false;
            }
            if (ok) typesOk++;

            // Live links include Anytype's own header and hidden relations;
            // the manifest speaks only of its own keys, so compare the live
            // list reduced to them, in live order.
            HashSet<string> declared = spec.Properties.ToHashSet(StringComparer.Ordinal);
            string[] liveOrder = type.PropertyLinks.Select(l => l.Key).Where(declared.Contains).ToArray();
            memberships += liveOrder.Length;
            string[] missing = spec.Properties.Where(k => !liveOrder.Contains(k)).ToArray();
            if (missing.Length > 0)
                mismatches.Add($"membership {spec.Key}: missing {string.Join(",", missing)}");
            else if (!liveOrder.SequenceEqual(spec.Properties))
                mismatches.Add($"order {spec.Key}: live [{string.Join(",", liveOrder)}], manifest [{string.Join(",", spec.Properties)}]");

            foreach (SchemaPropertyLinkInfo link in type.PropertyLinks.Where(l => declared.Contains(l.Key)))
                if (manifest.Properties.Single(p => p.Key == link.Key) is { } p && link.Format.Length > 0 && link.Format != p.Format)
                    mismatches.Add($"membership-format {spec.Key}.{link.Key}: {link.Format}, manifest {p.Format}");
        }

        Write($"Conformes : {typesOk}/{manifest.Types.Count} types, {propsOk}/{manifest.Properties.Count} propriétés, "
            + $"{optionsOk}/{manifest.Properties.Sum(p => p.Tags.Count)} options "
            + $"(dont {optionKeysOk} clé(s) d'option déclarée(s) retrouvée(s)), {memberships} appartenances de propriété vérifiées.");
        return mismatches;
    }
}
