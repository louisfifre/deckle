using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// Persons and companies over the v2 writer (FakeV2Handler) and the v1 property
// list (FakeAnytypeServer) the role is looked for in. Selectors are bafy* ids,
// so the resolver asks nothing.
[Trait("Category", "integration")]
public class ContactGesturesTests
{
    const string CompanyId = "bafyreicompanyaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string PersonId = "bafyreipersonaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string RolePropertyId = "bafyreirolepropertyaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static ContactGestures NewGestures(FakeAnytypeServer server, FakeV2Handler v2)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var v2Client = new AnytypeV2Client(
            new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"), null, v2);
        return new ContactGestures(client, new NameResolver(client), v2Client);
    }

    static JsonObject Page(params JsonObject[] rows) => new()
    {
        ["data"] = new JsonArray(rows),
        ["pagination"] = new JsonObject { ["has_more"] = false },
    };

    static void WithRoleSelect(FakeAnytypeServer server)
    {
        server.OnListProperties(Page(new JsonObject
        {
            ["id"] = RolePropertyId, ["key"] = "role", ["name"] = "Rôle", ["format"] = "select",
        }));
        server.OnListPropertyTags(RolePropertyId, Page(
            new JsonObject { ["key"] = "decision", ["name"] = "Décision" },
            new JsonObject { ["key"] = "technique", ["name"] = "Technique" }));
    }

    // The space of 2026-11-04: only the v5 link to the « Rôle » type.
    static void WithoutRoleSelect(FakeAnytypeServer server) =>
        server.OnListProperties(Page(new JsonObject
        {
            ["id"] = "bafyreirolelink", ["key"] = "role_lie", ["name"] = "Rôle", ["format"] = "objects",
        }));

    static JsonObject PostedBody(FakeV2Handler v2) =>
        (JsonObject)JsonNode.Parse(Assert.Single(v2.Requests, r => r.Method == "POST").Body!)!;

    [Fact]
    public async Task CreatePersonSendsTheCoordinatesTheCompanyAndTheRole()
    {
        using var server = new FakeAnytypeServer();
        WithRoleSelect(server);
        FakeV2Handler v2 = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{\"id\":\"" + CompanyId + "\",\"type\":\"entreprise\",\"properties\":{\"name\":\"Atelier\"}}")
            .Reply(HttpStatusCode.Created, "{\"id\":\"" + PersonId + "\"}");

        string digest = await NewGestures(server, v2).CreatePersonAsync(
            " Camille Martin ", email: "camille@example.test", phone: "01 02 03 04 05",
            company: CompanyId, role: "décision", ct: Ct);

        JsonObject body = PostedBody(v2);
        Assert.Equal(DevSpace.Types.Contact, body["type"]!.GetValue<string>());
        Assert.Equal("Camille Martin", body["name"]!.GetValue<string>());
        JsonObject props = body["properties"]!.AsObject();
        Assert.Equal("camille@example.test", props[DevSpace.Props.Courriel]!.GetValue<string>());
        Assert.Equal("01 02 03 04 05", props[DevSpace.Props.Telephone]!.GetValue<string>());
        Assert.Equal(CompanyId, props[DevSpace.Props.EntrepriseLiee]!.AsArray().Single()!.GetValue<string>());
        Assert.Equal("Décision", props["role"]!.AsArray().Single()!.GetValue<string>());

        Assert.Contains("Personne créée : Camille Martin", digest);
        Assert.Contains($"id : {PersonId}", digest);
        Assert.Contains("Entreprise : Atelier", digest);
        Assert.Contains("Rôle : Décision", digest);
    }

    // A person never needs a company, and a role the space cannot hold yet
    // does not stop the creation: the answer says it was not set.
    [Fact]
    public async Task CreatePersonWithoutACompanyAndWithoutARoleSelectStillCreatesThePerson()
    {
        using var server = new FakeAnytypeServer();
        WithoutRoleSelect(server);
        FakeV2Handler v2 = new FakeV2Handler().Reply(HttpStatusCode.Created, "{\"id\":\"" + PersonId + "\"}");

        string digest = await NewGestures(server, v2).CreatePersonAsync("Camille", role: "Décision", ct: Ct);

        JsonObject body = PostedBody(v2);
        Assert.False(body.ContainsKey("properties"));
        Assert.Contains("Rôle : non posé", digest);
    }

    [Fact]
    public async Task CreatePersonRefusesARoleTheSelectDoesNotOfferBeforeCreating()
    {
        using var server = new FakeAnytypeServer();
        WithRoleSelect(server);
        var v2 = new FakeV2Handler();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server, v2).CreatePersonAsync("Camille", role: "Cuisine", ct: Ct));

        Assert.Contains("Décision", error.Message);
        Assert.Empty(v2.Requests);
    }

    [Fact]
    public async Task CreateCompanySendsItsNameAndAddress()
    {
        using var server = new FakeAnytypeServer();
        FakeV2Handler v2 = new FakeV2Handler().Reply(HttpStatusCode.Created, "{\"id\":\"" + CompanyId + "\"}");

        string digest = await NewGestures(server, v2).CreateCompanyAsync("Atelier", address: "1 rue du Port", ct: Ct);

        JsonObject body = PostedBody(v2);
        Assert.Equal(DevSpace.Types.Entreprise, body["type"]!.GetValue<string>());
        Assert.Equal("1 rue du Port", body["properties"]![DevSpace.Props.Adresse]!.GetValue<string>());
        Assert.Contains("Entreprise créée : Atelier", digest);
        Assert.Empty(server.Requests);
    }
}
