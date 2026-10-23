using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for LiveTagResolver over the shared FakeAnytypeServer. The
// library cannot create a tag option: a value resolves to an EXISTING live
// option or throws, never reaching the wire.
//
// The resolver consults the space's live property + options endpoints and does
// NOT read DevSpace, so « tag » serves as the example property even though it
// carries no frozen vocabulary in the schema.
[Trait("Category", "integration")]
public class LiveTagResolverTests
{
    const string TagPropId = "bafyreiTagPropaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static LiveTagResolver NewResolver(FakeAnytypeServer server) =>
        new(new AnytypeApiClient(server.Credentials));

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

        string key = await NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "Urgent", Ct);

        Assert.Equal("urgent", key);
    }

    [Fact]
    public async Task UnknownValueThrowsListingTheValidOptions()
    {
        using var server = new FakeAnytypeServer();
        server.OnListProperties(PropertiesPage());
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "inconnu", Ct));

        Assert.Contains("inconnu", ex.Message);
        Assert.Contains("urgent", ex.Message);
    }

    // The server serves fewer rows per page than the 100 the resolver asks
    // for. The option wanted sits among the rows a limit-sized step would skip.
    const int ServerPageCap = 40;
    const int WantedIndex = 90;

    static IEnumerable<JsonObject> NumberedProperties(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "property",
            ["id"] = i == WantedIndex ? TagPropId : $"prop-{i:D4}",
            ["key"] = i == WantedIndex ? DevSpace.Props.Tag : $"key_{i:D4}",
            ["name"] = $"Property {i}",
            ["format"] = "multi_select",
        });

    static IEnumerable<JsonObject> NumberedTags(int count) =>
        Enumerable.Range(0, count).Select(i => new JsonObject
        {
            ["object"] = "tag",
            ["id"] = $"tag-{i:D4}",
            ["key"] = $"option_{i:D4}",
            ["name"] = $"Option {i}",
            ["color"] = "grey",
        });

    [Fact]
    [Trait("Category", "regression")]
    public async Task FindsThePropertyWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnPagedListProperties(NumberedProperties(130), ServerPageCap);
        server.OnListPropertyTags(TagPropId, TagsPage(("urgent", "Urgent")));

        string key = await NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "Urgent", Ct);

        Assert.Equal("urgent", key);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task FindsTheOptionWhenTheServerCapsThePage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListProperties(PropertiesPage());
        server.OnPagedListPropertyTags(TagPropId, NumberedTags(130), ServerPageCap);

        string key = await NewResolver(server).ResolveAsync(
            DevSpace.Props.Tag, $"Option {WantedIndex}", Ct);

        Assert.Equal($"option_{WantedIndex:D4}", key);
    }

    [Fact]
    [Trait("Category", "regression")]
    public async Task StopsReadingOptionsOnAnEmptyPageEvenIfTheServerStillClaimsMore()
    {
        using var server = new FakeAnytypeServer();
        server.OnListProperties(PropertiesPage());
        server.OnListPropertyTags(TagPropId, new JsonObject
        {
            ["data"] = new JsonArray(),
            ["pagination"] = new JsonObject { ["has_more"] = true },
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Ct, timeout.Token);

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewResolver(server).ResolveAsync(DevSpace.Props.Tag, "Urgent", bounded.Token));

        Assert.Contains("aucune", ex.Message);
    }
}
