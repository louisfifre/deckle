using System.Text.Json.Nodes;
using Deckle.Anytype.Api;
using Deckle.Anytype.Gestures;
using Deckle.Anytype.Schema;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for QueryGestures.UpdateAsync over the shared
// FakeAnytypeServer. They pin the owner's guarantee: the library cannot create a
// tag option. A select/multi_select value must resolve to an EXISTING option —
// frozen vocabularies in DevSpace, free (space-managed) ones against the live
// options endpoint — and an unknown value throws BEFORE any PATCH leaves.
//
// Selector is a bafy* id so the resolver short-circuits (no /search route).
[Trait("Category", "integration")]
public class QueryGesturesTests
{
    const string TaskId = "bafyreiTaskaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // The « tag » property is a free multi_select (no frozen vocabulary). Its live
    // id, as the property-list endpoint would return it.
    const string TagPropId = "bafyreiTagPropaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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

    // The space's property list, mapping the « tag » key to its id.
    static JsonObject PropertiesPage() => new()
    {
        ["data"] = new JsonArray
        {
            new JsonObject
            {
                ["object"] = "property",
                ["id"] = TagPropId,
                ["key"] = DevSpace.Props.Tag,
                ["name"] = "Tag",
                ["format"] = "multi_select",
            },
        },
        ["pagination"] = new JsonObject { ["has_more"] = false },
    };

    // A property's existing options, in the live endpoint's shape.
    static JsonObject TagsPage(params (string Key, string Name)[] options)
    {
        var data = new JsonArray();
        foreach ((string key, string name) in options)
            data.Add(new JsonObject
            {
                ["object"] = "tag",
                ["id"] = $"bafyreiTagId{key}",
                ["key"] = key,
                ["name"] = name,
                ["color"] = "grey",
            });
        return new JsonObject
        {
            ["data"] = data,
            ["pagination"] = new JsonObject { ["has_more"] = false },
        };
    }

    // (a) Unknown value on a free-vocabulary multi_select → throws, no PATCH sent.
    [Fact]
    public async Task UpdateWithUnknownFreeVocabularyValueThrowsAndSendsNoPatch()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnListProperties(PropertiesPage());
        // The property exists, with one option « urgent » — but the caller asks
        // for « inconnu », which names none.
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));

        var gestures = NewGestures(server);
        var props = new JsonObject { ["tag"] = "inconnu" };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => gestures.UpdateAsync(TaskId, props));

        // The error names the unknown value and lists the valid option — the
        // model-facing affordance.
        Assert.Contains("inconnu", ex.Message);
        Assert.Contains("urgent", ex.Message);

        // The guarantee: nothing was PATCHed, so the API was never asked to take
        // (and possibly auto-create) the unknown option.
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    // (b) A value matching a live option resolves; the PATCH carries the option key.
    [Fact]
    public async Task UpdateWithKnownFreeVocabularyValueResolvesToTheLiveOptionKey()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetObject(TaskId, TaskObject());
        server.OnListProperties(PropertiesPage());
        // The live option « Urgent » carries the wire key « urgent ».
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));
        server.OnPatchObject(TaskId, TaskObject());

        // Caller passes the DISPLAY NAME; it must resolve to the existing key.
        var props = new JsonObject { ["tag"] = "Urgent" };
        await NewGestures(server).UpdateAsync(TaskId, props);

        JsonObject patched = server.LastBodyFor("PATCH");
        var entries = Assert.IsType<JsonArray>(patched["properties"]);
        JsonObject entry = Assert.IsType<JsonObject>(Assert.Single(entries));
        Assert.Equal(DevSpace.Props.Tag, entry["key"]!.GetValue<string>());

        var values = Assert.IsType<JsonArray>(entry["multi_select"]);
        Assert.Equal("urgent", Assert.Single(values)!.GetValue<string>());
    }

    // (c) Frozen-vocabulary behavior is unchanged: « etat » resolves in memory, no
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
        await NewGestures(server).UpdateAsync(TaskId, props);

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
            () => NewGestures(server).UpdateAsync(TaskId, props));

        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }
}
