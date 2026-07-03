using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for LiveTagResolver over the shared FakeAnytypeServer. This
// is where the owner's guarantee now lives directly — the library cannot create
// a tag option: a value resolves to an EXISTING live option or throws, never
// reaching the wire. (It used to be exercised through QueryGestures.UpdateAsync
// via « tag », but tag is now mapped onto no type, so no update routes here.)
//
// The resolver consults the space's live property + options endpoints and does
// NOT read DevSpace, so « tag » remains a valid example property here even though
// it carries no frozen vocabulary in the schema.
[Trait("Category", "integration")]
public class LiveTagResolverTests
{
    const string TagPropId = "bafyreiTagPropaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static LiveTagResolver NewResolver(FakeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials));

    // The space's property list, mapping the « tag » key to its live id.
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

    [Fact]
    public async Task ResolvesADisplayNameToTheExistingOptionKey()
    {
        using var server = new FakeAnytypeServer();
        server.OnListProperties(PropertiesPage());
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));

        // Caller passes the DISPLAY NAME; it must resolve to the existing wire key.
        string key = await NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "Urgent", Ct);

        Assert.Equal("urgent", key);
    }

    [Fact]
    public async Task UnknownValueThrowsListingTheValidOptions()
    {
        using var server = new FakeAnytypeServer();
        server.OnListProperties(PropertiesPage());
        // The property exists with one option « urgent » — but the caller asks for
        // « inconnu », which names none, so the resolver throws rather than mint one.
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "inconnu", Ct));

        Assert.Contains("inconnu", ex.Message);
        Assert.Contains("urgent", ex.Message);
    }
}
