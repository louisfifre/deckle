using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Integration tests for DocumentGestures over the shared FakeAnytypeServer. The
// contract is the wire payload: a Deckle document is born as Anytype type
// `document` with an existing Type de document option, optional version, optional
// Document système, and optional initial body.
[Trait("Category", "integration")]
public class DocumentGesturesTests
{
    const string DocumentId = "bafyreidocumentaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static DocumentGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new DocumentGestures(client);
    }

    static JsonObject DocumentObject(string name) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = DocumentId,
            ["name"] = name,
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Document },
            ["properties"] = new JsonArray(),
        },
    };

    [Fact]
    public async Task CreateSendsDocumentTypeBodyAndResolvedDocumentType()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(DocumentObject("Architecture Anytype"));

        await NewGestures(server).CreateAsync(
            "Architecture Anytype", "Architecture", body: "# But\nCréer.", ct: Ct);

        JsonObject created = server.LastBodyFor("POST");
        Assert.Equal(DevSpace.Types.Document, created["type_key"]!.GetValue<string>());
        Assert.Equal("Architecture Anytype", created["name"]!.GetValue<string>());
        Assert.Equal("# But\nCréer.", created["body"]!.GetValue<string>());

        JsonArray props = Assert.IsType<JsonArray>(created["properties"]);
        JsonObject type = Assert.IsType<JsonObject>(Assert.Single(props));
        Assert.Equal(DevSpace.Props.TypeDeDocument, type["key"]!.GetValue<string>());
        Assert.Equal("architecture", type["select"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateWithVersionAndSystemSendsOptionalProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(DocumentObject("Instruction MCP"));

        await NewGestures(server).CreateAsync(
            "Instruction MCP", "instructions", version: "1.0", system: true, ct: Ct);

        JsonObject created = server.LastBodyFor("POST");
        JsonArray props = Assert.IsType<JsonArray>(created["properties"]);

        Assert.Contains(props, p =>
            p is JsonObject o
            && o["key"]?.GetValue<string>() == DevSpace.Props.TypeDeDocument
            && o["select"]?.GetValue<string>() == "instructions");

        Assert.Contains(props, p =>
            p is JsonObject o
            && o["key"]?.GetValue<string>() == DevSpace.Props.Version
            && o["text"]?.GetValue<string>() == "1.0");

        Assert.Contains(props, p =>
            p is JsonObject o
            && o["key"]?.GetValue<string>() == DevSpace.Props.DocumentSysteme
            && o["checkbox"]?.GetValue<bool>() == true);
    }

    [Fact]
    public async Task CreateWithUnknownDocumentTypeThrowsAndSendsNoPost()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostObject(DocumentObject("X"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).CreateAsync("X", "pas-un-type", ct: Ct));

        Assert.DoesNotContain(server.Requests, r => r.Method == "POST");
    }
}
