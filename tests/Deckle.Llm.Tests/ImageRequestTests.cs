using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Deckle.Llm.Tests;

// A request that carries an image goes only to a server on this machine.
// Anything else is refused before a byte is sent: the handler below records
// every request the engine would have sent, and stays empty.
[Trait("Category", "unit")]
public sealed class ImageRequestTests
{
    static readonly GenerationImage Picture = new([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg");

    static GenerationRequest WithImage() =>
        new(Model: "vision", SystemPrompt: "system", UserText: "describe", Label: "test", Image: Picture);

    public static TheoryData<string> NotLocal =>
    [
        "http://192.168.1.20:11434",
        "http://10.0.0.5:1234/v1",
        "https://api.openai.com/v1",
        "http://ollama.example.com:11434/api/generate",
        "http://localhost.example.com:11434",
        "http://127.0.0.1.nip.io:11434",
        "http://user@[::2]:11434",
        "ftp://127.0.0.1/",
        "not a url",
        "",
    ];

    [Theory]
    [MemberData(nameof(NotLocal))]
    public void OllamaRefusesAnImageForAnEndpointNotOnThisMachine(string endpoint)
    {
        var handler = new RecordingHandler();
        var engine = new OllamaEngine(endpoint, new HttpClient(handler));

        Assert.Throws<LocalEndpointRequiredException>(() => engine.Generate(WithImage(), CancellationToken.None));
        Assert.Empty(handler.Sent);
    }

    [Theory]
    [MemberData(nameof(NotLocal))]
    public void ChatCompletionsRefusesAnImageForAnEndpointNotOnThisMachine(string endpoint)
    {
        var handler = new RecordingHandler();
        var engine = new OpenAiCompatibleEngine(endpoint, () => "key", new HttpClient(handler));

        Assert.Throws<LocalEndpointRequiredException>(() => engine.Generate(WithImage(), CancellationToken.None));
        Assert.Empty(handler.Sent);
    }

    [Fact]
    public void ClaudeNeverAcceptsAnImage()
    {
        int keyReads = 0;
        var engine = new ClaudeEngine(() => { keyReads++; return "key"; });

        Assert.Throws<LocalEndpointRequiredException>(() => engine.Generate(WithImage(), CancellationToken.None));
        Assert.Equal(0, keyReads);
    }

    [Theory]
    [InlineData("http://localhost:11434/api/generate", "http://localhost:11434/api/chat")]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/api/chat")]
    [InlineData("http://[::1]:11434/api/chat", "http://[::1]:11434/api/chat")]
    public void OllamaSendsTheImageToItsChatEndpoint(string endpoint, string expectedUrl)
    {
        var handler = new RecordingHandler("""{"message":{"role":"assistant","content":" Un chat. "},"done_reason":"stop"}""");
        var engine = new OllamaEngine(endpoint, new HttpClient(handler));

        GenerationResult result = engine.Generate(WithImage(), CancellationToken.None);

        Assert.Equal("Un chat.", result.Text);
        (string url, JsonDocument body) = Assert.Single(handler.Sent);
        Assert.Equal(expectedUrl, url);
        JsonElement user = body.RootElement.GetProperty("messages")[1];
        Assert.Equal("user", user.GetProperty("role").GetString());
        Assert.Equal("describe", user.GetProperty("content").GetString());
        Assert.Equal(Picture.ToBase64(), user.GetProperty("images")[0].GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("raw", out _));
    }

    [Fact]
    public void ChatCompletionsSendsTheImageAsADataUriPart()
    {
        var handler = new RecordingHandler("""{"choices":[{"message":{"content":"Un chat."},"finish_reason":"stop"}]}""");
        var engine = new OpenAiCompatibleEngine("http://localhost:1234", () => null, new HttpClient(handler));

        GenerationResult result = engine.Generate(WithImage(), CancellationToken.None);

        Assert.Equal("Un chat.", result.Text);
        (string url, JsonDocument body) = Assert.Single(handler.Sent);
        Assert.Equal("http://localhost:1234/v1/chat/completions", url);
        JsonElement parts = body.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("describe", parts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.Equal("data:image/jpeg;base64," + Picture.ToBase64(),
            parts[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"model does not support images"}""")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"this model is missing data required for image input"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"image input is not supported - hint: you may need to provide the mmproj"}}""")]
    public void AServerThatCannotReadImagesIsATypedFailure(HttpStatusCode status, string body)
    {
        var handler = new RecordingHandler(body, status);
        var engine = new OpenAiCompatibleEngine("http://127.0.0.1:8080", () => null, new HttpClient(handler));

        Assert.Throws<ImageInputNotSupportedException>(() => engine.Generate(WithImage(), CancellationToken.None));
    }

    [Fact]
    public void AnUnknownModelStaysAnHttpFailureWithItsStatus()
    {
        var handler = new RecordingHandler("""{"error":"model 'vision' not found, try pulling it first"}""", HttpStatusCode.NotFound);
        var engine = new OllamaEngine("http://localhost:11434", new HttpClient(handler));

        var ex = Assert.Throws<HttpRequestException>(() => engine.Generate(WithImage(), CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://LOCALHOST:11434", true)]
    [InlineData("https://127.0.0.1", true)]
    [InlineData("http://127.4.5.6:80", true)]
    [InlineData("http://[::1]:1234/v1", true)]
    [InlineData("http://[::ffff:127.0.0.1]:1234", true)]
    [InlineData("http://192.168.0.2", false)]
    [InlineData("http://[::ffff:192.168.0.2]:1234", false)]
    [InlineData("http://localhost.evil.test", false)]
    [InlineData("http://0.0.0.0:11434", false)]
    [InlineData(null, false)]
    public void LoopbackMeansThisMachineOnly(string? endpoint, bool expected) =>
        Assert.Equal(expected, LocalEndpoint.IsLoopback(endpoint));

    // Stands in for the network: records what the engine sends and answers
    // with a fixed body. Nothing leaves the process.
    sealed class RecordingHandler(string answer = "{}", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Url, JsonDocument Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            Sent.Add((request.RequestUri!.ToString(), JsonDocument.Parse(body)));
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
