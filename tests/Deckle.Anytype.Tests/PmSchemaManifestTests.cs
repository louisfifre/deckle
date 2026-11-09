using System.Reflection;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The v6 manifest of the project-management space must load through the real
// schema-admin parser, and DevSpace, the keys the gestures send, must say exactly
// what the manifest says: the same types, the same properties in the same order,
// the same formats, labels and select options. The space is built from the
// manifest and used through DevSpace, so any drift between the two is a tool that
// breaks on the day the space is recreated.
[Trait("Category", "unit")]
public sealed class PmSchemaManifestTests
{
    const string ResourceName = "Deckle.Anytype.Schema.pm-schema-manifest.json";

    // The v5 keys of the space of 2026-11-04 and the v6 key each one became. It
    // documents the rename; the test below holds the new side to the manifest.
    static readonly Dictionary<string, string> KeyRename = new()
    {
        ["etat"] = "status",
        ["priorite"] = "priority",
        ["definition_de_fini"] = "done_definition",
        ["budget_estime"] = "budget_estimated",
        ["budget_reel_("] = "budget_actual",
        ["charge_estimee"] = "effort_estimated",
        ["charge_estimee_(jours)"] = "effort_actual",
        ["relation_projet"] = "projects",
        ["contact_lie"] = "contacts",
        ["fichier(s)_lie(s)"] = "files",
        ["type_de_tache"] = "task_type",
        ["tache(s)_liee(s)"] = "tasks",
        ["depend_de"] = "depends_on",
        ["type_de_document"] = "document_type",
        ["document_systeme"] = "document_global",
        ["entreprise_liee"] = "company",
        ["courriel"] = "email",
        ["numero_de_telephone"] = "phone",
        ["adresse"] = "address",
        ["rapport"] = "report",
        ["idee"] = "idea",
        ["contact"] = "person",
        ["entreprise"] = "company",
    };

    static JsonObject LoadRaw()
    {
        using Stream stream = typeof(SchemaManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    static SchemaManifest Load() => SchemaManifest.Parse(LoadRaw());

    static IReadOnlyList<string> PropertiesOf(SchemaManifest manifest, string typeKey) =>
        manifest.Types.Single(t => t.Key == typeKey).Properties;

    static IEnumerable<string> ConstantValues(Type holder) =>
        holder.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void ManifestParsesWithTheSchemaAdminLoader()
    {
        SchemaManifest manifest = Load();

        Assert.Equal(
            ["epic", "project", "task", "report", "idea", "document", "person", "company"],
            manifest.Types.Select(t => t.Key));
    }

    [Fact]
    public void EveryTypePropertyIsDeclaredInThePropertiesArray()
    {
        SchemaManifest manifest = Load();
        HashSet<string> declared = manifest.Properties.Select(p => p.Key).ToHashSet();

        foreach (TypeSpec type in manifest.Types)
            foreach (string key in type.Properties)
                Assert.True(declared.Contains(key), $"{type.Key} lists « {key} » which no property declares.");
    }

    [Fact]
    public void DevSpaceCarriesTheManifestTypesInTheSameOrder()
    {
        Assert.Equal(Load().Types.Select(t => t.Key), DevSpace.TypeKeys);
        Assert.Equal(DevSpace.TypeKeys.Order(StringComparer.Ordinal), ConstantValues(typeof(DevSpace.Types)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTypeListsTheSamePropertiesInTheSameOrderAsTheManifest()
    {
        SchemaManifest manifest = Load();

        foreach (TypeSpec type in manifest.Types)
            Assert.Equal(
                type.Properties,
                DevSpace.PropertiesFor(type.Key).Select(p => p.Key));
    }

    [Fact]
    public void EveryDevSpacePropertySaysWhatTheManifestSays()
    {
        SchemaManifest manifest = Load();

        foreach (TypeSpec type in manifest.Types)
            foreach (PropertyDef def in DevSpace.PropertiesFor(type.Key))
            {
                PropertySpec spec = manifest.Properties.Single(p => p.Key == def.Key);

                Assert.True(spec.Name == def.Label, $"{type.Key}.{def.Key}: label « {def.Label} », manifest « {spec.Name} ».");
                Assert.True(spec.Format == def.Format, $"{type.Key}.{def.Key}: format {def.Format}, manifest {spec.Format}.");

                // A select carries its options in DevSpace and in the manifest,
                // key, name and order alike.
                IReadOnlyList<(string Key, string Name)> declared =
                    spec.Tags.Select(t => (t.Key ?? t.MatchKey, t.Name)).ToList();
                IReadOnlyList<(string Key, string Name)> frozen =
                    (def.Options ?? []).Select(o => (o.Key, o.Name)).ToList();
                Assert.True(declared.SequenceEqual(frozen), $"{type.Key}.{def.Key}: options differ from the manifest.");
                if (spec.Tags.Count > 0)
                    Assert.All(spec.Tags, t => Assert.True(t.Key is not null, $"{def.Key}: option « {t.Name} » has no declared key."));
            }
    }

    [Fact]
    public void EveryPropertyKeyDevSpaceNamesIsInTheManifestAndTheOtherWayRound()
    {
        SchemaManifest manifest = Load();
        HashSet<string> declared = manifest.Properties.Select(p => p.Key).ToHashSet();

        // « tag » is Anytype's own transversal property, outside the manifest.
        HashSet<string> named = ConstantValues(typeof(DevSpace.Props)).Where(k => k != DevSpace.Props.Tag).ToHashSet();

        Assert.Equal(declared.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryKeyTheV5SpaceHadIsRenamedToAKeyBothSidesKnow()
    {
        SchemaManifest manifest = Load();
        HashSet<string> keys = manifest.Properties.Select(p => p.Key)
            .Concat(manifest.Types.Select(t => t.Key))
            .ToHashSet();

        foreach ((string was, string became) in KeyRename)
            Assert.True(keys.Contains(became), $"« {was} » became « {became} », which the manifest does not declare.");
    }

    [Fact]
    public void ProjectAndTaskKeepTheOrderTheModelDeclares()
    {
        SchemaManifest manifest = Load();

        Assert.Equal(
            ["status", "priority", "done_definition", "due_date", "budget_estimated", "budget_actual",
             "effort_estimated", "effort_actual", "epic", "depends_on", "documents", "contacts", "archive"],
            PropertiesOf(manifest, "project"));
        Assert.Equal(
            ["done", "task_type", "priority", "due_date", "slot", "done_definition", "projects", "documents",
             "contacts", "time_estimated", "time_actual", "files", "status", "archive"],
            PropertiesOf(manifest, "task"));
    }

    [Fact]
    public void AProjectHasNoDoneBoxAndOnlyTheProjectStatesAreSelectable()
    {
        SchemaManifest manifest = Load();

        Assert.DoesNotContain("done", PropertiesOf(manifest, "project"));
        Assert.Equal(
            ["Prêt", "En cours", "En pause", "Dormant"],
            manifest.Properties.Single(p => p.Key == "status").Tags.Select(t => t.Name));
    }

    [Fact]
    public void SlotAndRoleKeysPointAtDeclaredSelects()
    {
        SchemaManifest manifest = Load();

        Assert.Equal(DevSpace.Slot.Default, manifest.Properties.Single(p => p.Key == "slot").Tags[0].Name);
        Assert.Equal(DevSpace.Slot.Name, manifest.Properties.Single(p => p.Key == DevSpace.Slot.Keys[0]).Name);
        Assert.Equal(DevSpace.Role.Name, manifest.Properties.Single(p => p.Key == DevSpace.Role.Keys[0]).Name);
    }
}
