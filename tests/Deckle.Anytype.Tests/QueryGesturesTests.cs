using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for QueryGestures.UpdateAsync over the shared
// FakeAnytypeServer. They pin two guarantees: a select value must resolve to an
// EXISTING option (frozen vocabularies in DevSpace, an unknown value throwing
// before any PATCH), and the now-unmapped « tag » property is refused outright.
// The free-vocabulary live-resolution path itself is exercised directly in
// LiveTagResolverTests (no mapped property routes there since « tag » was dropped).
//
// Selector is a bafy* id so the resolver short-circuits (no /search route).
[Trait("Category", "integration")]
public class QueryGesturesTests
{
    const string TaskId = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static QueryGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new QueryGestures(client, new NameResolver(client));
    }

    // GET response for the task the update targets — a task so « tag » (free) and
    // « etat » (frozen) both apply.
    static JsonObject TaskObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Ma tâche",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            ["properties"] = new JsonArray(),
        },
    };

    // A task object carrying a markdown body — the surface replace_section reads,
    // splices, and reads back.
    static JsonObject BodyObject(string markdown) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Ma tâche",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Task },
            ["markdown"] = markdown,
            ["properties"] = new JsonArray(),
        },
    };

    // A rapport GET response — the type that does NOT carry the « Archivé »
    // checkbox, so archive must refuse it.
    static JsonObject RapportObject() => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = TaskId,
            ["name"] = "Un rapport",
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Rapport },
            ["properties"] = new JsonArray(),
        },
    };

    // « tag » is intentionally mapped onto no type (Anytype's auto-transversal
    // residue, unused). update must therefore REFUSE it before any write — both a
    // regression guard on the schema decision and the reason the live-resolution
    // path is no longer reached through update.
    [Fact]
    public async Task UpdateRefusesTheUnmappedTagPropertyAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        var props = new JsonObject { ["tag"] = "urgent" };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, props));

        Assert.Contains("tag", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // Frozen-vocabulary behavior is unchanged: « etat » resolves in memory, no
    // live lookup, and an unknown value still throws with no PATCH.
    [Fact]
    public async Task UpdateOnFrozenVocabularyResolvesInMemoryWithoutALiveLookup()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());
        // Deliberately register NO /properties or /tags route: a frozen vocabulary
        // must resolve without touching the live options endpoint.

        var props = new JsonObject { ["etat"] = "En cours" };
        await NewGestures(server).UpdateAsync(TaskId, null, props);

        JsonObject patched = server.LastBodyFor("PATCH");
        var entries = Assert.IsType<JsonArray>(patched["properties"]);
        JsonObject entry = Assert.IsType<JsonObject>(Assert.Single(entries));
        Assert.Equal(DevSpace.Props.Etat, entry["key"]!.GetValue<string>());
        Assert.Equal("en_cours", entry["select"]!.GetValue<string>());

        // No live-options endpoint was hit — frozen path stays in memory.
        Assert.DoesNotContain(server.Requests, r => r.Path.Contains("/properties"));
    }

    [Fact]
    public async Task UpdateOnFrozenVocabularyWithUnknownValueThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        var props = new JsonObject { ["etat"] = "pas-un-etat" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, props));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // ── UpdateAsync — rename ──────────────────────────────────────────────────

    // A name-only update PATCHes the new title at the payload ROOT (mirroring
    // create) and carries no `properties` array — nothing else was asked.
    [Fact]
    public async Task UpdateWithNameOnlyPatchesTheTitleAtTheRootWithoutProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).UpdateAsync(TaskId, "Nouveau titre", null);

        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal("Nouveau titre", patched["name"]!.GetValue<string>());
        Assert.False(patched.ContainsKey("properties"));
    }

    // Name AND properties land in ONE PATCH: the title at the root, the resolved
    // property entries under `properties` — never two round-trips.
    [Fact]
    public async Task UpdateWithNameAndPropertiesComposesASinglePatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        var props = new JsonObject { ["etat"] = "En cours" };
        await NewGestures(server).UpdateAsync(TaskId, "Nouveau titre", props);

        // Exactly one PATCH carried both the root name and the property entries.
        Assert.Equal(1, server.Requests.Count(r => r.Method == "PATCH"));
        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal("Nouveau titre", patched["name"]!.GetValue<string>());
        JsonObject entry = Assert.IsType<JsonObject>(Assert.Single((JsonArray)patched["properties"]!));
        Assert.Equal(DevSpace.Props.Etat, entry["key"]!.GetValue<string>());
        Assert.Equal("en_cours", entry["select"]!.GetValue<string>());
    }

    // A blank name is a shape error refused before any write.
    [Fact]
    public async Task UpdateWithABlankNameThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, "   ", null));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // Neither name nor properties is a shape error refused before any write — and
    // before any GET, since the request is empty on its face.
    [Fact]
    public async Task UpdateWithNeitherNameNorPropertiesThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).UpdateAsync(TaskId, null, null));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // rapport is body-titled (title = first line of its body), so a rename on it is
    // refused before any write, pointing the model at replace_section.
    [Fact]
    public async Task UpdateRenamingARapportIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, RapportObject());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).UpdateAsync(TaskId, "Un autre titre", null));

        Assert.Contains("replace_section", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // ── ArchiveAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ArchiveSetsTheArchivedCheckboxTrue()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).ArchiveAsync(TaskId);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject entry = Assert.IsType<JsonObject>(((JsonArray)patched["properties"]!).Single());
        Assert.Equal(DevSpace.Props.Archive, entry["key"]!.GetValue<string>());
        Assert.True(entry["checkbox"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ArchiveWithValueFalseRestoresTheObject()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnPatchObject(TaskId, TaskObject());

        await NewGestures(server).ArchiveAsync(TaskId, value: false);

        JsonObject patched = server.LastBodyFor("PATCH");
        JsonObject entry = Assert.IsType<JsonObject>(((JsonArray)patched["properties"]!).Single());
        Assert.False(entry["checkbox"]!.GetValue<bool>());
    }

    // A rapport carries no « Archivé » checkbox (it stays searchable on purpose):
    // archive refuses it before any write, naming the checkbox in the message.
    [Fact]
    public async Task ArchiveOnARapportIsRefusedAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, RapportObject());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ArchiveAsync(TaskId));

        Assert.Contains("Archivé", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // ── ReplaceSectionAsync ─────────────────────────────────────────────────────

    // The headline path: the targeted section is spliced (siblings copied
    // verbatim), the PATCH echoes Anytype's re-rendered body — underscore escaped,
    // hard-break trailing spaces — and verification still confirms the intent
    // through that normalization, so no second GET is needed.
    [Fact]
    public async Task ReplaceSectionSplicesTheTargetAndVerifiesThroughEscapingWithoutASecondRead()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, BodyObject("# Tâche\n\n## État\nÀ faire.\n\n## Notes\nrien"));
        server.OnPatchObject(TaskId, BodyObject(
            "# Tâche   \n\n## État   \nEn cours sur module\\_X.   \n\n## Notes   \nrien   "));

        string digest = await NewGestures(server)
            .ReplaceSectionAsync(TaskId, "État", "En cours sur module_X.");

        // The PATCH carried the spliced document: the target body replaced, the
        // « Notes » section copied byte-for-byte.
        JsonObject patched = server.LastBodyFor("PATCH");
        Assert.Equal(
            "# Tâche\n\n## État\nEn cours sur module_X.\n\n## Notes\nrien",
            patched["markdown"]!.GetValue<string>());

        Assert.Contains("vérifié", digest);
        // Verification used the PATCH echo, so exactly one GET (the RMW read) ran.
        Assert.Equal(1, server.Requests.Count(r => r.Method == "GET"));
    }

    [Fact]
    public async Task ReplaceSectionWithAnAbsentHeadingThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, BodyObject("## Présent\ncorps"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ReplaceSectionAsync(TaskId, "Absent", "x"));

        // Strict, and helpful: the message names the present headings to retry with.
        Assert.Contains("introuvable", ex.Message);
        Assert.Contains("Présent", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task ReplaceSectionWithARepeatedHeadingThrowsAmbiguousAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, BodyObject("## Doublon\na\n## Doublon\nb"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ReplaceSectionAsync(TaskId, "Doublon", "x"));

        Assert.Contains("ambig", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // The write commits but the read-back does not confirm it (here the echo still
    // shows the old content). The gesture must report the divergence, not claim
    // success — a full-replacement PATCH cannot be rolled back.
    [Fact]
    public async Task ReplaceSectionReportsDivergenceWhenTheReadBackDoesNotConfirmTheIntent()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, BodyObject("## État\nancien"));
        server.OnPatchObject(TaskId, BodyObject("## État\nancien"));

        string digest = await NewGestures(server).ReplaceSectionAsync(TaskId, "État", "nouveau");

        Assert.Contains("vérification en échec", digest);
    }

    // Removing a sub-heading that lived inside the replaced section is intended, not
    // loss — the section-set guard keys on the intended body, so this still verifies.
    [Fact]
    public async Task ReplaceSectionDroppingAnInSectionSubHeadingStillVerifies()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, BodyObject("## Groupe\n### enfant\nx\n## Fin\nz"));
        server.OnPatchObject(TaskId, BodyObject("## Groupe   \nrésumé   \n## Fin   \nz   "));

        string digest = await NewGestures(server).ReplaceSectionAsync(TaskId, "Groupe", "résumé");

        Assert.Contains("vérifié", digest);
    }
}
