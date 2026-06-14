using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public class DialogueGesturesTests
{
    const string ChatId = "bafyreiChatdialogueaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string TaskId = "bafyreiTaskdialogueaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string MessageId = "bafyreiMessagedialogueaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static DialogueGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        return new DialogueGestures(client, new NameResolver(client));
    }

    [Fact]
    public async Task CreateCreatesChatLinksOptionalTaskAndPostsTheBrief()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostChat(ChatObject("LLM - Challenge"));
        server.OnPatchObject(ChatId, ChatObject("LLM - Challenge", TaskId));
        server.OnPostChatMessage(ChatId, MessageResponse(MessageId));

        string digest = await NewGestures(server).CreateAsync(
            "LLM - Challenge",
            "challenge",
            "Hypothèse à attaquer.",
            TaskId,
            TestContext.Current.CancellationToken);

        JsonObject created = BodyFor(server, "POST", $"/v1/spaces/{FakeAnytypeServer.Space}/chats");
        Assert.Equal("LLM - Challenge", created["name"]!.GetValue<string>());

        JsonObject patched = BodyFor(server, "PATCH", $"/v1/spaces/{FakeAnytypeServer.Space}/objects/{ChatId}");
        JsonObject prop = Assert.IsType<JsonObject>(Assert.Single((JsonArray)patched["properties"]!));
        Assert.Equal(DevSpace.Props.TachesLiees, prop["key"]!.GetValue<string>());
        Assert.Equal(TaskId, Assert.Single((JsonArray)prop["objects"]!)!.GetValue<string>());

        JsonObject posted = BodyFor(server, "POST", $"/v1/spaces/{FakeAnytypeServer.Space}/chats/{ChatId}/messages");
        string text = posted["text"]!.GetValue<string>();
        Assert.Contains("[System]", text);
        Assert.Contains("Mode : challenge", text);
        Assert.Contains("Hypothèse à attaquer.", text);
        Assert.Contains(ChatId, digest);
    }

    [Fact]
    public async Task PostPrefixesTheRequestedSpeaker()
    {
        using var server = new FakeAnytypeServer();
        server.OnPostChatMessage(ChatId, MessageResponse(MessageId));

        await NewGestures(server).PostAsync(
            ChatId,
            "codex",
            "Je challenge.",
            TestContext.Current.CancellationToken);

        JsonObject posted = BodyFor(server, "POST", $"/v1/spaces/{FakeAnytypeServer.Space}/chats/{ChatId}/messages");
        Assert.Equal("[Codex]\nJe challenge.", posted["text"]!.GetValue<string>());
        Assert.Equal("paragraph", posted["style"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReadReturnsMessagesAndTheLastOrderId()
    {
        using var server = new FakeAnytypeServer();
        server.OnGetChatMessages(ChatId, MessagesResponse(
            Message("!!a", "Louis", "Salut"),
            Message("!!b", "Louis", "[Codex]\nJe réponds.")));

        string digest = await NewGestures(server).ReadAsync(
            ChatId,
            ct: TestContext.Current.CancellationToken);

        Assert.Contains($"chat_id : {ChatId}", digest);
        Assert.Contains("dernier_order_id : !!b", digest);
        Assert.Contains("[Louis]\nSalut", digest);
        Assert.Contains("[Codex]\nJe réponds.", digest);
    }

    static JsonObject ChatObject(string name, params string[] linkedTasks) => new()
    {
        ["object"] = new JsonObject
        {
            ["id"] = ChatId,
            ["name"] = name,
            ["type"] = new JsonObject { ["key"] = DevSpace.Types.Chat },
            ["properties"] = linkedTasks.Length == 0
                ? new JsonArray()
                : new JsonArray(new JsonObject
                {
                    ["key"] = DevSpace.Props.TachesLiees,
                    ["objects"] = ToArray(linkedTasks),
                }),
        },
    };

    static JsonObject MessageResponse(string messageId) => new()
    {
        ["message_id"] = messageId,
    };

    static JsonObject MessagesResponse(params JsonObject[] messages)
    {
        var array = new JsonArray();
        foreach (JsonObject message in messages) array.Add(message);
        return new JsonObject { ["messages"] = array };
    }

    static JsonObject Message(string orderId, string creator, string text) => new()
    {
        ["id"] = "message-" + orderId,
        ["order_id"] = orderId,
        ["creator_name"] = creator,
        ["content"] = new JsonObject
        {
            ["text"] = text,
            ["style"] = "paragraph",
        },
    };

    static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }

    static JsonObject BodyFor(FakeAnytypeServer server, string method, string path)
    {
        var request = server.Requests.Last(r => r.Method == method && r.Path == path);
        return (JsonObject)JsonNode.Parse(request.Body)!;
    }
}
