using System.Reflection;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The v6 manifest of the project-management space must load through the real
// schema-admin parser, and must stay answerable to DevSpace, the keys the
// gestures speak today. The two differ on purpose (the recreation renames the
// space's French keys to English ones); the table below is that rename, so a key
// DevSpace gains without a decision here fails the test.
[Trait("Category", "unit")]
public sealed class PmSchemaManifestTests
{
    const string ResourceName = "Deckle.Anytype.Schema.pm-schema-manifest.json";

    // DevSpace type key -> manifest type key.
    static readonly Dictionary<string, string> TypeRename = new()
    {
        [DevSpace.Types.Epic] = "epic",
        [DevSpace.Types.Project] = "project",
        [DevSpace.Types.Task] = "task",
        [DevSpace.Types.Rapport] = "report",
        [DevSpace.Types.Idee] = "idea",
        [DevSpace.Types.Document] = "document",
        [DevSpace.Types.Contact] = "person",
        [DevSpace.Types.Entreprise] = "company",
    };

    // DevSpace property key -> manifest property key; null when model v6 drops it.
    static readonly Dictionary<string, string?> PropertyRename = new()
    {
        [DevSpace.Props.Etat] = "status",
        [DevSpace.Props.Priorite] = "priority",
        [DevSpace.Props.DefinitionDeFini] = "done_definition",
        [DevSpace.Props.BudgetEstime] = "budget_estimated",
        [DevSpace.Props.BudgetReel] = "budget_actual",
        [DevSpace.Props.ChargeEstimee] = "effort_estimated",
        [DevSpace.Props.ChargeReelle] = "effort_actual",
        [DevSpace.Props.Archive] = "archive",
        [DevSpace.Props.Version] = "version",
        [DevSpace.Props.DueDate] = "due_date",
        [DevSpace.Props.Done] = "done",
        [DevSpace.Props.DependDe] = "depends_on",
        [DevSpace.Props.RelationProjet] = "projects",
        [DevSpace.Props.ContactLie] = "contacts",
        [DevSpace.Props.FichiersLies] = "files",
        [DevSpace.Props.TypeDeTache] = "task_type",
        [DevSpace.Props.TachesLiees] = "tasks",
        [DevSpace.Props.TypeDeDocument] = "document_type",
        [DevSpace.Props.DocumentSysteme] = "document_global",
        [DevSpace.Props.EntrepriseLiee] = "company",
        [DevSpace.Props.Courriel] = "email",
        [DevSpace.Props.Telephone] = "phone",
        [DevSpace.Props.Adresse] = "address",
        // Dropped by model v6.
        [DevSpace.Props.Description] = null,
        [DevSpace.Props.PhaseProjet] = null,
        [DevSpace.Props.Livrables] = null,
        [DevSpace.Props.DateDuJournal] = null,
    };

    // A key that survives elsewhere but model v6 takes off one type: the case
    // « done » and « version » on a project (the archive closes it; version
    // stays on Document).
    // On a task the model measures time, not effort: the two charge keys of
    // DevSpace become time_estimated and time_actual there.
    static readonly Dictionary<(string DevType, string DevKey), string> TypeSpecificRename = new()
    {
        [(DevSpace.Types.Task, DevSpace.Props.ChargeEstimee)] = "time_estimated",
        [(DevSpace.Types.Task, DevSpace.Props.ChargeReelle)] = "time_actual",
    };

    static readonly HashSet<(string DevType, string DevKey)> DroppedFromType =
    [
        (DevSpace.Types.Project, DevSpace.Props.Done),
        (DevSpace.Types.Project, DevSpace.Props.Version),
        // The model's Report section lists tasks, contacts and Archivé only.
        (DevSpace.Types.Rapport, DevSpace.Props.FichiersLies),
    ];

    static JsonObject LoadRaw()
    {
        using Stream stream = typeof(SchemaManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    static SchemaManifest Load() => SchemaManifest.Parse(LoadRaw());

    static IReadOnlyList<string> PropertiesOf(SchemaManifest manifest, string typeKey) =>
        manifest.Types.Single(t => t.Key == typeKey).Properties;

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
    public void EveryKeyDevSpaceCarriesIsRenamedOrDroppedOnPurpose()
    {
        SchemaManifest manifest = Load();

        foreach ((string devType, string manifestType) in TypeRename)
        {
            IReadOnlyList<string> listed = PropertiesOf(manifest, manifestType);
            foreach (PropertyDef def in DevSpace.PropertiesFor(devType))
            {
                Assert.True(PropertyRename.TryGetValue(def.Key, out string? renamed),
                    $"DevSpace key « {def.Key} » ({devType}) has no entry in the rename table.");
                if (TypeSpecificRename.TryGetValue((devType, def.Key), out string? specific))
                    renamed = specific;
                if (renamed is not null && !DroppedFromType.Contains((devType, def.Key)))
                    Assert.True(listed.Contains(renamed),
                        $"{manifestType} lacks « {renamed} » (DevSpace « {def.Key} » on {devType}).");
            }
        }
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
    public void SharedVocabulariesKeepDevSpaceNamesWhereTheModelKeepsThem()
    {
        SchemaManifest manifest = Load();
        IReadOnlyList<string> Names(string key) =>
            manifest.Properties.Single(p => p.Key == key).Tags.Select(t => t.Name).ToList();

        Assert.Equal(DevSpace.Priority.All.Select(o => o.Name), Names("priority"));
        Assert.Equal(
            DevSpace.TypeDeTache.All.Select(o => o.Name).Order(StringComparer.Ordinal),
            Names("task_type").Order(StringComparer.Ordinal));
        Assert.Equal(DevSpace.Slot.Default, Names("slot")[0]);
        Assert.Equal(DevSpace.Slot.Name, manifest.Properties.Single(p => p.Key == "slot").Name);
        Assert.Equal(DevSpace.Role.Name, manifest.Properties.Single(p => p.Key == "role").Name);
    }
}
