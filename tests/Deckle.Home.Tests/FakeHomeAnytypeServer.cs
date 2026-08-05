using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Deckle.Anytype;
using Deckle.Home;
using Deckle.TestSupport;

namespace Deckle.Home.Tests;

internal sealed class FakeHomeAnytypeServer : IDisposable
{
    public const string HomeSpace = "home-test-space";
    private const string DevSpace = "dev-test-space";

    private readonly LoopbackHttpListenerLease _listenerLease;
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly Task _loop;
    private readonly ConcurrentQueue<Received> _requests = new();
    private readonly List<JsonObject> _objects = new();
    private JsonObject _schemaManifest = HomeSchema.CreateRequiredSchemaManifest();
    private int _nextId;

    public readonly record struct Received(string Method, string Path, string Body);

    public FakeHomeAnytypeServer()
    {
        _listenerLease = LoopbackHttpListenerLease.Start();
        _prefix = _listenerLease.Prefix;
        _listener = _listenerLease.Listener;
        _loop = Task.Run(ServeAsync);
    }

    public AnytypeCredentials Credentials =>
        new(_prefix.TrimEnd('/'), "2025-11-08", "test-key", DevSpace);

    public IReadOnlyList<Received> Requests => _requests.ToArray();

    public void SetObjects(params JsonObject[] values)
    {
        _objects.Clear();
        _objects.AddRange(values);
    }

    public void RemoveSchemaProperty(string key)
    {
        JsonArray properties = (JsonArray)_schemaManifest["properties"]!;
        JsonObject? target = properties.OfType<JsonObject>()
            .SingleOrDefault(value => value["key"]?.GetValue<string>() == key);
        if (target is not null) properties.Remove(target);
    }

    public static JsonObject Room(string id, string code, string name) => Object(
        id, HomeSchema.Types.Room, $"{code} — {name}");

    public static JsonObject Element(string id, string code, string roomId) => Object(
        id, HomeSchema.Types.Outlet, code,
        ObjectsProperty(HomeSchema.Properties.Room, "Pièce", roomId),
        SelectProperty(HomeSchema.Properties.Category, "Catégorie", "p", "Prise 230 V"),
        SelectProperty(HomeSchema.Properties.Existence, "Existence", "existant", "Existant"));

    public static JsonObject Circuit(string id, string code) => Object(
        id, HomeSchema.Types.Circuit, code);

    public static JsonObject Idea(string id, string name) => Object(
        id, HomeSchema.Types.Idea, name);

    public static JsonObject Errand(string id, string name, bool done) => Object(
        id, HomeSchema.Types.Errand, name,
        CheckboxProperty("done", "Done", done));

    public static JsonObject Worksite(string id, string name) => Object(
        id, HomeSchema.Types.Worksite, name,
        SelectProperty(HomeSchema.Properties.Status, "Statut", "en_cours", "En cours"));

    public static JsonObject WorkTask(string id, string name, string? worksiteId, bool done) =>
        worksiteId is null
            ? Object(
                id, HomeSchema.Types.Task, name,
                CheckboxProperty("done", "Done", done))
            : Object(
                id, HomeSchema.Types.Task, name,
                CheckboxProperty("done", "Done", done),
                ObjectsProperty(HomeSchema.Properties.Worksite, "Chantier", worksiteId));

    public static JsonObject Collection(string id, string name) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["layout"] = "collection",
        ["type"] = new JsonObject { ["key"] = "etage", ["layout"] = "collection" },
        ["properties"] = new JsonArray(),
    };

    private static JsonObject Object(
        string id, string type, string name, params JsonObject[] properties) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = new JsonObject { ["key"] = type },
        ["properties"] = new JsonArray(properties),
    };

    private static JsonObject TextProperty(string key, string name, string value) => new()
    {
        ["key"] = key, ["name"] = name, ["format"] = "text", ["text"] = value,
    };

    private static JsonObject CheckboxProperty(string key, string name, bool value) => new()
    {
        ["key"] = key, ["name"] = name, ["format"] = "checkbox", ["checkbox"] = value,
    };

    private static JsonObject ObjectsProperty(string key, string name, params string[] ids) => new()
    {
        ["key"] = key, ["name"] = name, ["format"] = "objects",
        ["objects"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
    };

    private static JsonObject SelectProperty(
        string key, string name, string tagKey, string tagName) => new()
    {
        ["key"] = key, ["name"] = name, ["format"] = "select",
        ["select"] = new JsonObject { ["key"] = tagKey, ["name"] = tagName },
    };

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();
            string path = context.Request.Url!.AbsolutePath;
            string method = context.Request.HttpMethod;
            _requests.Enqueue(new Received(method, path, body));

            (int status, string json) = Handle(method, path, body);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        }
    }

    private (int Status, string Json) Handle(string method, string path, string body)
    {
        string root = $"/v1/spaces/{HomeSpace}";
        if (method == "GET" && path == root + "/types") return (200, SchemaTypes().ToJsonString());
        if (method == "GET" && path == root + "/properties") return (200, SchemaProperties().ToJsonString());
        if (method == "GET" && path.StartsWith(root + "/properties/", StringComparison.Ordinal)
            && path.EndsWith("/tags", StringComparison.Ordinal))
            return (200, SchemaTags(path.Split('/')[^2]).ToJsonString());
        if (method == "GET" && path == root + "/objects") return (200, ObjectPage().ToJsonString());
        if (method == "POST" && path == root + "/objects") return Create(body);
        if (method == "POST" && path.StartsWith(root + "/lists/", StringComparison.Ordinal)
            && path.EndsWith("/objects", StringComparison.Ordinal))
            return (200, "\"ok\"");
        if (method == "PATCH" && path.StartsWith(root + "/objects/", StringComparison.Ordinal))
            return Patch(path.Split('/')[^1], body);
        if (method == "DELETE" && path.StartsWith(root + "/lists/", StringComparison.Ordinal)
            && path.Contains("/objects/", StringComparison.Ordinal))
            return (200, "\"ok\"");
        if (method == "DELETE" && path.StartsWith(root + "/objects/", StringComparison.Ordinal))
            return Delete(path.Split('/')[^1]);
        return (404, "{}");
    }

    private JsonObject SchemaTypes()
    {
        var data = new JsonArray();
        foreach (JsonObject spec in ((JsonArray)_schemaManifest["types"]!).OfType<JsonObject>())
        {
            var properties = new JsonArray();
            foreach (JsonNode? property in (JsonArray)spec["properties"]!)
                properties.Add(property!.GetValue<string>());
            data.Add(new JsonObject
            {
                ["id"] = "type-" + spec["key"]!.GetValue<string>(),
                ["key"] = spec["key"]!.GetValue<string>(),
                ["name"] = spec["name"]!.GetValue<string>(),
                ["plural_name"] = spec["plural_name"]!.GetValue<string>(),
                ["layout"] = spec["layout"]!.GetValue<string>(),
                ["properties"] = properties,
            });
        }
        return Page(data);
    }

    private JsonObject SchemaProperties()
    {
        var data = new JsonArray();
        foreach (JsonObject spec in ((JsonArray)_schemaManifest["properties"]!).OfType<JsonObject>())
        {
            string key = spec["key"]!.GetValue<string>();
            data.Add(new JsonObject
            {
                ["id"] = "prop-" + key,
                ["key"] = key,
                ["name"] = spec["name"]!.GetValue<string>(),
                ["format"] = spec["format"]!.GetValue<string>(),
            });
        }
        return Page(data);
    }

    private JsonObject SchemaTags(string propertyId)
    {
        string propertyKey = propertyId["prop-".Length..];
        JsonObject? property = ((JsonArray)_schemaManifest["properties"]!).OfType<JsonObject>()
            .SingleOrDefault(value => value["key"]?.GetValue<string>() == propertyKey);
        var data = new JsonArray();
        if (property?["tags"] is JsonArray tags)
            foreach (JsonObject tag in tags.OfType<JsonObject>())
            {
                string key = tag["key"]!.GetValue<string>();
                data.Add(new JsonObject
                {
                    ["id"] = "tag-" + propertyKey + "-" + key,
                    ["key"] = key,
                    ["name"] = tag["name"]!.GetValue<string>(),
                    ["color"] = "grey",
                });
            }
        return Page(data);
    }

    private JsonObject ObjectPage()
    {
        var data = new JsonArray();
        foreach (JsonObject value in _objects) data.Add(value.DeepClone());
        return Page(data);
    }

    private (int Status, string Json) Create(string body)
    {
        JsonObject payload = (JsonObject)JsonNode.Parse(body)!;
        string id = "created-" + Interlocked.Increment(ref _nextId);
        var created = new JsonObject
        {
            ["id"] = id,
            ["name"] = payload["name"]?.GetValue<string>() ?? "",
            ["type"] = new JsonObject { ["key"] = payload["type_key"]?.GetValue<string>() ?? "" },
            ["properties"] = payload["properties"]?.DeepClone(),
        };
        _objects.Add(created);
        return (200, new JsonObject { ["object"] = created.DeepClone() }.ToJsonString());
    }

    private (int Status, string Json) Patch(string id, string body)
    {
        JsonObject? target = _objects.SingleOrDefault(value => value["id"]?.GetValue<string>() == id);
        if (target is null) return (404, "{}");
        JsonObject payload = (JsonObject)JsonNode.Parse(body)!;
        if (payload["name"] is JsonValue name) target["name"] = name.GetValue<string>();
        if (payload["properties"] is JsonArray properties) target["properties"] = properties.DeepClone();
        return (200, new JsonObject { ["object"] = target.DeepClone() }.ToJsonString());
    }

    private (int Status, string Json) Delete(string id)
    {
        JsonObject? target = _objects.SingleOrDefault(value => value["id"]?.GetValue<string>() == id);
        if (target is null) return (404, "{}");
        _objects.Remove(target);
        return (200, new JsonObject { ["object"] = target.DeepClone() }.ToJsonString());
    }

    private static JsonObject Page(JsonArray data) => new()
    {
        ["data"] = data,
        ["pagination"] = new JsonObject { ["has_more"] = false },
    };

    public void Dispose()
    {
        _listenerLease.Dispose();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}
