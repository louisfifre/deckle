using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.TestSupport;

namespace Deckle.Travel.Tests;

// A loopback Anytype standing in for a Travel space provisioned from the
// module's own manifest: the type and property routes are GENERATED from
// TravelSchema.CreateRequiredSchemaManifest(), so a gesture test runs against
// exactly the schema schema-admin would apply — not against a hand-written
// fixture that can drift from the contract.
//
// One deliberate infidelity, which is the measured truth: tag options answer
// under an OPAQUE provider key, never the manifest's. Anytype derives option
// keys from the label at creation, so a test that echoed the manifest keys back
// would silently bless an assumption the live API does not honor.
internal sealed class FakeTravelSpace : IDisposable
{
    public const string Space = "test-space";

    private readonly LoopbackHttpListenerLease _lease;
    private readonly List<Route> _routes = [];
    private readonly ConcurrentQueue<Received> _received = new();
    private readonly Task _loop;

    private sealed record Route(string Method, string Path, int Status, string Json);

    public readonly record struct Received(string Method, string Path, string Body);

    public FakeTravelSpace()
    {
        _lease = LoopbackHttpListenerLease.Start();
        _loop = Task.Run(ServeAsync);

        JsonObject manifest = TravelSchema.CreateRequiredSchemaManifest();
        Add("GET", "/types", Page(TypeRows(manifest)));
        Add("GET", "/properties", Page(PropertyRows(manifest)));
        foreach (JsonObject property in Rows(manifest, "properties"))
        {
            if (property["tags"] is not JsonArray tags) continue;
            string key = property["key"]!.GetValue<string>();
            Add("GET", $"/properties/{PropertyId(key)}/tags", Page(TagRows(key, tags)));
        }
    }

    public AnytypeApiClient NewClient() =>
        new(new AnytypeCredentials(_lease.Prefix.TrimEnd('/'), "2025-11-08", "test-key", Space));

    public TravelGestures NewGestures() => new(NewClient(), Space);

    public static string PropertyId(string key) => $"prop-{key}";

    // ── Route registration ──────────────────────────────────────────────

    public void OnListObjects(params JsonObject[] objects) =>
        Add("GET", "/objects", Page([.. objects]));

    public void OnUploadFile(string objectId, string name) =>
        Add("POST", "/files", new JsonObject
        {
            ["object_id"] = objectId,
            ["name"] = name,
            ["media"] = "application/octet-stream",
        });

    public void OnPatchObject(string id, JsonObject response) =>
        Add("PATCH", $"/objects/{id}", response);

    // ── Request introspection ───────────────────────────────────────────

    public IReadOnlyList<Received> Requests => [.. _received];

    public Received Last(string method, string path)
    {
        Received[] matches = [.. _received.Where(r => r.Method == method && r.Path == $"/v1/spaces/{Space}{path}")];
        return matches.Length > 0
            ? matches[^1]
            : throw new InvalidOperationException($"No {method} {path} request was received.");
    }

    public JsonObject LastBody(string method, string path) =>
        (JsonObject)JsonNode.Parse(Last(method, path).Body)!;

    // ── Manifest → wire rows ────────────────────────────────────────────

    private static IEnumerable<JsonObject> Rows(JsonObject manifest, string field) =>
        ((JsonArray)manifest[field]!).Select(node => (JsonObject)node!);

    private static List<JsonObject> TypeRows(JsonObject manifest) =>
    [
        .. Rows(manifest, "types").Select(type =>
        {
            string key = type["key"]!.GetValue<string>();
            var links = new JsonArray();
            foreach (JsonNode? attached in (JsonArray)type["properties"]!)
            {
                string propertyKey = attached!.GetValue<string>();
                links.Add(new JsonObject { ["id"] = PropertyId(propertyKey), ["key"] = propertyKey });
            }
            return new JsonObject
            {
                ["id"] = $"type-{key}",
                ["key"] = key,
                ["name"] = type["name"]!.DeepClone(),
                ["plural_name"] = type["plural_name"]!.DeepClone(),
                ["layout"] = "basic",
                ["properties"] = links,
            };
        }),
    ];

    private static List<JsonObject> PropertyRows(JsonObject manifest) =>
    [
        .. Rows(manifest, "properties").Select(property =>
        {
            string key = property["key"]!.GetValue<string>();
            return new JsonObject
            {
                ["id"] = PropertyId(key),
                ["key"] = key,
                ["name"] = property["name"]!.DeepClone(),
                ["format"] = property["format"]!.DeepClone(),
            };
        }),
    ];

    private static List<JsonObject> TagRows(string propertyKey, JsonArray tags)
    {
        var rows = new List<JsonObject>();
        int index = 0;
        foreach (JsonNode? node in tags)
        {
            var tag = (JsonObject)node!;
            index++;
            rows.Add(new JsonObject
            {
                ["id"] = $"tag-{propertyKey}-{index}",
                ["key"] = $"opt{index}",  // provider-derived, never the manifest key
                ["name"] = tag["name"]!.DeepClone(),
                ["color"] = "grey",
            });
        }
        return rows;
    }

    private static JsonObject Page(List<JsonObject> rows)
    {
        var data = new JsonArray();
        foreach (JsonObject row in rows) data.Add(row);
        return new JsonObject
        {
            ["data"] = data,
            ["pagination"] = new JsonObject { ["has_more"] = false },
        };
    }

    // ── Serve loop ──────────────────────────────────────────────────────

    private void Add(string method, string path, JsonObject response) =>
        _routes.Add(new Route(method, $"/v1/spaces/{Space}{path}", 200, response.ToJsonString()));

    private async Task ServeAsync()
    {
        while (_lease.Listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _lease.Listener.GetContextAsync(); }
            catch { return; }

            string method = ctx.Request.HttpMethod;
            string path = ctx.Request.Url!.AbsolutePath;
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();
            _received.Enqueue(new Received(method, path, body));

            Route? route = _routes.LastOrDefault(r => r.Method == method && r.Path == path);
            byte[] payload = Encoding.UTF8.GetBytes(route?.Json ?? "{}");
            ctx.Response.StatusCode = route?.Status ?? 404;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = payload.Length;
            await ctx.Response.OutputStream.WriteAsync(payload);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _lease.Dispose();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
