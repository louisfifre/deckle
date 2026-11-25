using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.TestSupport;

namespace Deckle.Anytype.Mcp.Tests;

// A small Anytype space behind a loopback HTTP listener, for tests that call a
// tool of the real catalog the way an agent does and then look at what the
// space holds. It keeps objects (type, name, properties, markdown) and answers
// the few routes the tools reach, in the shape the live API was measured to
// use: v2 object reads and block appends, v2 create and search, v1 property
// writes. A route it does not know answers 404, so a tool reaching somewhere
// unexpected fails loudly instead of hanging.
internal sealed class FakeAnytypeSpace : IDisposable
{
    public const string Space = "test-space";

    public sealed class Stored(string id, string type, string name)
    {
        public string Id { get; } = id;
        public string Type { get; } = type;
        public string Name { get; set; } = name;

        // Property key to its value as v2 reads it back: an array of ids or
        // option names, a bool, a number, a date or a string.
        public Dictionary<string, JsonNode?> Properties { get; } = new(StringComparer.Ordinal);

        public string Markdown { get; set; } = "";

        public IReadOnlyList<string> Ids(string key) =>
            Properties.TryGetValue(key, out JsonNode? node) && node is JsonArray array
                ? array.Select(n => n!.GetValue<string>()).ToArray()
                : [];

        public bool Checkbox(string key) =>
            Properties.TryGetValue(key, out JsonNode? node) && node?.GetValue<bool>() == true;

        public double? Number(string key) =>
            Properties.TryGetValue(key, out JsonNode? node) && node is not null ? node.GetValue<double>() : null;

        public string? Text(string key) =>
            Properties.TryGetValue(key, out JsonNode? node) && node is not null ? node.GetValue<string>() : null;
    }

    public sealed record Received(string Method, string Path, string Query, string Body);

    readonly LoopbackHttpListenerLease _lease;
    readonly ConcurrentDictionary<string, Stored> _objects = new(StringComparer.Ordinal);
    readonly ConcurrentQueue<Received> _received = new();
    readonly Task _loop;
    int _created;

    public FakeAnytypeSpace()
    {
        _lease = LoopbackHttpListenerLease.Start();
        _loop = Task.Run(ServeAsync);
    }

    public AnytypeCredentials Credentials =>
        new(_lease.Prefix.TrimEnd('/'), "2025-11-08", "test-key", Space);

    public IReadOnlyList<Received> Requests => _received.ToArray();

    public IReadOnlyCollection<Stored> Objects => _objects.Values.ToArray();

    public Stored this[string id] => _objects[id];

    public Stored Add(string id, string type, string name, string markdown = "",
        params (string Key, JsonNode? Value)[] properties)
    {
        var stored = new Stored(id, type, name) { Markdown = markdown };
        foreach ((string key, JsonNode? value) in properties) stored.Properties[key] = value;
        _objects[id] = stored;
        return stored;
    }

    public static JsonArray Ids(params string[] ids)
    {
        var array = new JsonArray();
        foreach (string id in ids) array.Add(id);
        return array;
    }

    string V2Objects => $"/v2/spaces/{Space}/objects";
    string V1Objects => $"/v1/spaces/{Space}/objects";

    async Task ServeAsync()
    {
        while (_lease.Listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _lease.Listener.GetContextAsync(); }
            catch { return; }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();
            string path = context.Request.Url!.AbsolutePath;
            var received = new Received(context.Request.HttpMethod, path, context.Request.Url.Query, body);
            _received.Enqueue(received);

            (int status, string json) = Answer(received);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        }
    }

    (int Status, string Json) Answer(Received r)
    {
        string? v2Id = IdUnder(r.Path, V2Objects);
        string? v1Id = IdUnder(r.Path, V1Objects);

        if (r.Method == "GET" && v2Id is not null && _objects.TryGetValue(v2Id, out Stored? read))
            return (200, V2Read(read).ToJsonString());

        if (r.Method == "PATCH" && v2Id is not null && _objects.TryGetValue(v2Id, out Stored? appended))
        {
            foreach (JsonNode? op in JsonNode.Parse(r.Body)!["ops"]!.AsArray())
                if (op?["op"]?.GetValue<string>() == "insert_blocks")
                    appended.Markdown += (appended.Markdown.Length > 0 ? "\n\n" : "")
                        + op["markdown"]!.GetValue<string>();
            return (200, "{}");
        }

        if (r.Method == "POST" && r.Path == V2Objects)
            return (201, Create(JsonNode.Parse(r.Body)!.AsObject()).ToJsonString());

        if (r.Method == "POST" && r.Path == $"/v2/spaces/{Space}/search")
            return (200, Search(JsonNode.Parse(r.Body)!.AsObject()).ToJsonString());

        if (r.Method == "GET" && v1Id is not null && _objects.TryGetValue(v1Id, out Stored? v1Read))
            return (200, new JsonObject { ["object"] = V1Object(v1Read) }.ToJsonString());

        if (r.Method == "PATCH" && v1Id is not null && _objects.TryGetValue(v1Id, out Stored? written))
        {
            ApplyV1Patch(written, JsonNode.Parse(r.Body)!.AsObject());
            return (200, new JsonObject { ["object"] = V1Object(written) }.ToJsonString());
        }

        // The space carries no optional property such as the slot.
        if (r.Method == "GET" && r.Path == $"/v1/spaces/{Space}/properties")
            return (200, """{"data":[],"pagination":{"has_more":false}}""");

        return (404, """{"message":"not found"}""");
    }

    static string? IdUnder(string path, string prefix) =>
        path.StartsWith(prefix + "/", StringComparison.Ordinal) ? path[(prefix.Length + 1)..] : null;

    static JsonObject V2Read(Stored stored)
    {
        var properties = new JsonObject { ["name"] = stored.Name };
        foreach ((string key, JsonNode? value) in stored.Properties)
            properties[key] = value?.DeepClone();

        // The outline holds one block per paragraph, the first heading apart.
        var outline = new JsonArray();
        foreach (string paragraph in stored.Markdown.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            outline.Add(new JsonObject
            {
                ["id"] = $"b{outline.Count}",
                ["type"] = paragraph.StartsWith('#') ? "heading_1" : "paragraph",
            });

        return new JsonObject
        {
            ["id"] = stored.Id,
            ["type"] = stored.Type,
            ["properties"] = properties,
            ["markdown"] = stored.Markdown,
            ["outline"] = outline,
        };
    }

    static JsonObject V1Object(Stored stored) => new()
    {
        ["id"] = stored.Id,
        ["name"] = stored.Name,
        ["type"] = new JsonObject { ["key"] = stored.Type },
        ["properties"] = new JsonArray(),
    };

    // A v1 property write is {key, <format>: value}: the value is the field
    // that is not the key.
    static void ApplyV1Patch(Stored stored, JsonObject patch)
    {
        if (patch["name"] is JsonValue name) stored.Name = name.GetValue<string>();
        foreach (JsonNode? node in patch["properties"] as JsonArray ?? [])
        {
            if (node is not JsonObject entry) continue;
            string key = entry["key"]!.GetValue<string>();
            string field = entry.Select(p => p.Key).First(k => k != "key");
            stored.Properties[key] = entry[field]?.DeepClone();
        }
    }

    JsonObject Create(JsonObject body)
    {
        // An id has the shape of a real one: base32 letters, no digit 0 or 1.
        string id = ("bafyreinew" + (char)('a' + Interlocked.Increment(ref _created))).PadRight(52, 'z');
        var stored = new Stored(id, body["type"]!.GetValue<string>(), body["name"]?.GetValue<string>() ?? "")
        {
            Markdown = body["markdown"]?.GetValue<string>() ?? "",
        };
        foreach ((string key, JsonNode? value) in body["properties"] as JsonObject ?? [])
            stored.Properties[key] = value?.DeepClone();
        _objects[id] = stored;
        return new JsonObject { ["id"] = id };
    }

    // The searches the tools send: a type and, at most, one `in` filter on a
    // relation. The filter is applied the way the server applies it.
    JsonObject Search(JsonObject body)
    {
        string? type = body["type"]?.GetValue<string>();
        var rows = new JsonArray();
        foreach (Stored stored in _objects.Values)
        {
            if (type is not null && stored.Type != type) continue;
            bool matches = true;
            foreach (JsonNode? filter in body["filters"] as JsonArray ?? [])
            {
                string property = filter!["property"]!.GetValue<string>();
                var wanted = filter["value"]!.AsArray().Select(n => n!.GetValue<string>());
                matches &= stored.Ids(property).Intersect(wanted).Any();
            }
            if (!matches) continue;

            var properties = new JsonObject();
            foreach ((string key, JsonNode? value) in stored.Properties)
                properties[key] = value?.DeepClone();
            rows.Add(new JsonObject
            {
                ["id"] = stored.Id, ["name"] = stored.Name, ["type"] = stored.Type, ["properties"] = properties,
            });
        }
        return new JsonObject { ["data"] = rows, ["total"] = rows.Count, ["has_more"] = false };
    }

    public void Dispose()
    {
        _lease.Dispose();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
