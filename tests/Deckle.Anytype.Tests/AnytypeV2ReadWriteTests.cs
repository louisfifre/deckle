using System.Net;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

// The reader and the writer through the same fake backend as the transport.
[Trait("Category", "unit")]
public class AnytypeV2ReadWriteTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnytypeV2Client ClientOver(FakeV2Handler handler) =>
        new(new AnytypeCredentials("http://127.0.0.1:31012", "2025-11-08", "test-bearer", "space-1"), null, handler);

    // ---- reader ----

    [Fact]
    public async Task TheReaderTakesTheEtagFromTheHeader()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, """{"id":"o1"}""", ("ETag", "\"h1\""));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2ObjectDocument document = await new AnytypeV2ObjectReader(client).ReadAsync("o1", ct: Ct);

        Assert.Equal("\"h1\"", document.ETag);
        Assert.Equal("/v2/spaces/space-1/objects/o1", Assert.Single(handler.Requests).PathAndQuery);
    }

    [Fact]
    public async Task TheReaderFallsBackToABodyEtagOnlyWhenNoHeaderCame()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, """{"etag":"\"b1\""}""")
            .Reply(HttpStatusCode.OK, """{"etag":"\"b2\""}""", ("ETag", "\"h2\""));
        using AnytypeV2Client client = ClientOver(handler);
        var reader = new AnytypeV2ObjectReader(client);

        Assert.Equal("\"b1\"", (await reader.ReadAsync("o1", ct: Ct)).ETag);
        Assert.Equal("\"h2\"", (await reader.ReadAsync("o1", ct: Ct)).ETag);
    }

    [Fact]
    public async Task TheReaderTurnsTheOptionsIntoTheQuery()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, "{}")
            .Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);
        var reader = new AnytypeV2ObjectReader(client);

        await reader.ReadAsync("o1", new AnytypeV2ObjectReadOptions { Outline = true }, Ct);
        await reader.ReadAsync("o1", new AnytypeV2ObjectReadOptions { Block = "b9", FullIds = true }, Ct);
        await reader.ReadAsync(
            "o1", new AnytypeV2ObjectReadOptions { Parts = AnytypeV2ObjectParts.Properties | AnytypeV2ObjectParts.Blocks, Markdown = true }, Ct);

        Assert.EndsWith("objects/o1?outline=true", handler.Requests[0].PathAndQuery);
        Assert.Contains("block=b9", handler.Requests[1].PathAndQuery);
        Assert.Contains("ids=full", handler.Requests[1].PathAndQuery);
        Assert.Contains("include=properties%2Cblocks", handler.Requests[2].PathAndQuery);
        Assert.Contains("format=md", handler.Requests[2].PathAndQuery);
    }

    [Fact]
    public async Task AnOutlineAndASingleBlockCannotBeAskedTogether()
    {
        var handler = new FakeV2Handler();
        using AnytypeV2Client client = ClientOver(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AnytypeV2ObjectReader(client).ReadAsync(
                "o1", new AnytypeV2ObjectReadOptions { Outline = true, Block = "b1" }, Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ANonObjectRootIsRefused()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "[]");
        using AnytypeV2Client client = ClientOver(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new AnytypeV2ObjectReader(client).ReadAsync("o1", ct: Ct));
    }

    // ---- writer ----

    private static IReadOnlyList<AnytypeV2Op> OneOp() =>
        [AnytypeV2BlockOps.SetChecked(AnytypeV2BlockRef.ById("b1"), true)];

    [Fact]
    public async Task AnObjectBatchIsAPatchWithTheOpsEnvelopeAndAnAutomaticIdempotencyKey()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, """{"object":{}}""", ("ETag", "\"w1\""));
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2WriteReceipt receipt = await new AnytypeV2Writer(client).ApplyObjectOpsAsync("o1", OneOp(), ct: Ct);

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal("PATCH", seen.Method);
        Assert.Equal("/v2/spaces/space-1/objects/o1", seen.PathAndQuery);
        Assert.Equal(
            """{"ops":[{"op":"update_block","id":"b1","set":{"checked":true}}]}""",
            seen.Body);
        Assert.False(string.IsNullOrWhiteSpace(seen.Header("Idempotency-Key")));
        Assert.Equal("\"w1\"", receipt.ETag);
    }

    [Fact]
    public async Task TwoWritesGetTwoDifferentGeneratedKeysAndACallerKeyIsKept()
    {
        var handler = new FakeV2Handler()
            .Reply(HttpStatusCode.OK, "{}").Reply(HttpStatusCode.OK, "{}").Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);
        var writer = new AnytypeV2Writer(client);

        await writer.ApplyObjectOpsAsync("o1", OneOp(), ct: Ct);
        await writer.ApplyObjectOpsAsync("o1", OneOp(), ct: Ct);
        await writer.ApplyObjectOpsAsync("o1", OneOp(), new AnytypeV2WriteOptions { IdempotencyKey = "mine" }, Ct);

        Assert.NotEqual(handler.Requests[0].Header("Idempotency-Key"), handler.Requests[1].Header("Idempotency-Key"));
        Assert.Equal("mine", handler.Requests[2].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task IfMatchAndTheQueryOptionsReachTheWire()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, """{"dry_run":true}""");
        using AnytypeV2Client client = ClientOver(handler);

        await new AnytypeV2Writer(client).ApplyObjectOpsAsync(
            "o1", OneOp(),
            new AnytypeV2WriteOptions { IfMatch = "\"r1\"", DryRun = true, FullIds = true }, Ct);

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal("\"r1\"", seen.Header("If-Match"));
        Assert.Contains("dry_run=true", seen.PathAndQuery);
        Assert.Contains("ids=full", seen.PathAndQuery);
    }

    [Fact]
    public async Task ATypeBatchIsAPatchOnTheTypeAndCanCreateMissingOptions()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.OK, "{}");
        using AnytypeV2Client client = ClientOver(handler);

        await new AnytypeV2Writer(client).ApplyTypeOpsAsync(
            "task",
            [AnytypeV2TypeOps.AddProperty("Season", AnytypeV2PropertyFormat.Select, options: ["Winter"])],
            new AnytypeV2WriteOptions { CreateMissingOptions = true }, Ct);

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal("PATCH", seen.Method);
        Assert.StartsWith("/v2/spaces/space-1/types/task", seen.PathAndQuery);
        Assert.Contains("create_missing_options=true", seen.PathAndQuery);
    }

    [Fact]
    public async Task ABatchForTheOtherEndpointIsRefusedBeforeItCostsARequest()
    {
        var handler = new FakeV2Handler();
        using AnytypeV2Client client = ClientOver(handler);
        var writer = new AnytypeV2Writer(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.ApplyTypeOpsAsync("task", OneOp(), ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.ApplyObjectOpsAsync("o1", [AnytypeV2TypeOps.RemoveProperty("p")], ct: Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ACreateIsAPostOfTheCallersBodyAndRefusesIfMatch()
    {
        var handler = new FakeV2Handler().Reply(HttpStatusCode.Created, """{"object":{"id":"n1"}}""");
        using AnytypeV2Client client = ClientOver(handler);
        var writer = new AnytypeV2Writer(client);
        var body = new JsonObject { ["type_key"] = "task", ["name"] = "A" };

        await writer.CreateObjectAsync(body, ct: Ct);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            writer.CreateObjectAsync(body, new AnytypeV2WriteOptions { IfMatch = "\"x\"" }, Ct));

        FakeV2Handler.Seen seen = Assert.Single(handler.Requests);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("/v2/spaces/space-1/objects", seen.PathAndQuery);
        Assert.Equal("""{"type_key":"task","name":"A"}""", seen.Body);
        Assert.NotNull(body["type_key"]); // the caller's node stays usable
    }

    [Fact]
    public async Task AFailedOpNamesItsIndexThroughTheWriter()
    {
        var handler = new FakeV2Handler().Reply(
            HttpStatusCode.BadRequest,
            """{"code":"validation_failed","issues":[{"path":"ops[0].id","message":"no such block"}]}""");
        using AnytypeV2Client client = ClientOver(handler);

        AnytypeV2Exception error = await Assert.ThrowsAsync<AnytypeV2Exception>(() =>
            new AnytypeV2Writer(client).ApplyObjectOpsAsync("o1", OneOp(), ct: Ct));

        Assert.Equal(0, error.OpIndex);
    }

    // ---- receipt ----

    [Fact]
    public void ATypeDefaultTemplateIsReadFromTheTopLevelReceipt()
    {
        var receipt = new AnytypeV2WriteReceipt(
            201,
            JsonNode.Parse("""{"template":{"id":"t1","name":"Default","source":"type_default"}}""")!.AsObject(),
            null, false, null);

        Assert.NotNull(receipt.Template);
        Assert.True(receipt.Template!.IsTypeDefault);
        Assert.Equal("t1", receipt.Template.Id);
        Assert.Equal("Default", receipt.Template.Name);
    }

    [Fact]
    public void ANamedTemplateIsNotATypeDefaultAndNoReceiptMeansNoTemplate()
    {
        var named = new AnytypeV2WriteReceipt(
            201, JsonNode.Parse("""{"template":{"id":"t1","source":"request"}}""")!.AsObject(), null, false, null);
        var none = new AnytypeV2WriteReceipt(201, new JsonObject(), null, false, null);
        var noBody = new AnytypeV2WriteReceipt(204, null, null, false, null);

        Assert.False(named.Template!.IsTypeDefault);
        Assert.Equal(AnytypeV2TemplateReceipt.RequestSource, named.Template.Source);
        Assert.Null(none.Template);
        Assert.Null(noBody.Template);
        Assert.False(noBody.WasDryRun);
    }

    [Fact]
    public void ADryRunReceiptExposesWhatWouldChange()
    {
        var receipt = new AnytypeV2WriteReceipt(
            200,
            JsonNode.Parse(
                """{"dry_run":true,"created_blocks":{"ops[0].blocks[0]":"x1"},"diff_stats":{"blocks_added":1,"blocks_removed":0}}""")!.AsObject(),
            null, false, null);

        Assert.True(receipt.WasDryRun);
        Assert.Equal("x1", receipt.CreatedBlocks!["ops[0].blocks[0]"]!.GetValue<string>());
        Assert.Equal(1, receipt.DiffStats!["blocks_added"]!.GetValue<int>());
    }
}
