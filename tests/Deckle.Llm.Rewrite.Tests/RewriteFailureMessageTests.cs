using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

[Trait("Category", "unit")]
public sealed class RewriteFailureMessageTests
{
    static readonly LlmProvider Ollama = new()
    {
        Id = "ollama", Name = "Ollama", Kind = LlmProviderKind.Ollama,
        Endpoint = "http://localhost:11434/api/generate",
    };

    static readonly LlmProvider LmStudio = new()
    {
        Id = "lm-studio", Name = "LM Studio", Kind = LlmProviderKind.OpenAiCompatible,
        Endpoint = "http://studio.lan:1234/v1",
    };

    static readonly LlmProvider Claude = new() { Id = "claude", Name = "Claude", Kind = LlmProviderKind.Claude };

    static RewriteProfile Profile(string providerId = "claude", string model = "claude-xyz") =>
        new() { Name = "Lissage", ProviderId = providerId, Model = model };

    [Fact]
    public void ConnectionRefusedSaysTheLocalServerIsNotAnsweringAtItsHostAndPort()
    {
        var ex = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "No connection could be made because the target machine actively refused it.",
            new SocketException((int)SocketError.ConnectionRefused));

        var message = RewriteFailureMessage.ForException(Profile("ollama"), Ollama, ex);

        Assert.Equal("Rewriter unavailable", message.Title);
        Assert.Equal("Ollama is not answering at localhost:11434. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void UnknownHostSaysTheProviderCannotBeReachedAtItsHost()
    {
        var ex = new HttpRequestException(
            HttpRequestError.NameResolutionError,
            "No such host is known.",
            new SocketException((int)SocketError.HostNotFound));

        var message = RewriteFailureMessage.ForException(Profile("lm-studio"), LmStudio, ex);

        Assert.Equal("LM Studio cannot be reached at studio.lan:1234. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void EndpointWithoutSchemeStillYieldsHostAndPort()
    {
        var provider = new LlmProvider
        {
            Id = "ollama", Name = "Ollama", Kind = LlmProviderKind.Ollama, Endpoint = "localhost:11434",
        };
        var ex = new HttpRequestException(
            HttpRequestError.ConnectionError, "refused", new SocketException((int)SocketError.ConnectionRefused));

        var message = RewriteFailureMessage.ForException(Profile("ollama"), provider, ex);

        Assert.Equal("Ollama is not answering at localhost:11434. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void ConnectionFailureWrappedByAnSdkIsStillFound()
    {
        // An SDK's I/O exception over HttpClient's: the walk reaches the
        // socket error however deep the engine's transport buries it.
        var ex = new IOException(
            "I/O error",
            new HttpRequestException(
                HttpRequestError.NameResolutionError,
                "No such host is known.",
                new SocketException((int)SocketError.HostNotFound)));

        var message = RewriteFailureMessage.ForException(Profile(), Claude, ex);

        Assert.Equal("Claude cannot be reached. Raw transcript copied.", message.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void RejectedKeyNamesTheProvider(HttpStatusCode status)
    {
        var message = RewriteFailureMessage.ForException(Profile(), Claude, EngineRefusal(status));

        Assert.Equal("Claude rejected the API key. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void RateLimitNamesTheProvider()
    {
        var message = RewriteFailureMessage.ForException(
            Profile(), Claude, EngineRefusal(HttpStatusCode.TooManyRequests));

        Assert.Equal("Claude is rate limiting requests. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void NotFoundNamesTheModel()
    {
        var message = RewriteFailureMessage.ForException(
            Profile(), Claude, EngineRefusal(HttpStatusCode.NotFound));

        Assert.Equal("Claude does not know model claude-xyz. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void ServerErrorGivesTheStatus()
    {
        var message = RewriteFailureMessage.ForException(
            Profile(), Claude, EngineRefusal((HttpStatusCode)529));

        Assert.Equal("Claude answered a server error (HTTP 529). Raw transcript copied.", message.Body);
    }

    [Fact]
    public void RenamedClaudeProviderIsNamedAsSettingsShowsIt()
    {
        var renamed = new LlmProvider { Id = "claude", Name = "Anthropic perso", Kind = LlmProviderKind.Claude };

        var message = RewriteFailureMessage.ForException(
            Profile(), renamed, EngineRefusal(HttpStatusCode.Unauthorized));

        Assert.Equal("Anthropic perso rejected the API key. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void OllamaUnknownModelFromEnsureSuccessStatusCodeNamesTheModel()
    {
        var ex = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).", null, HttpStatusCode.NotFound);

        var message = RewriteFailureMessage.ForException(Profile("ollama", "mistral-small3.2"), Ollama, ex);

        Assert.Equal("Ollama does not know model mistral-small3.2. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void ChatCompletionsRejectedKeyNamesTheProviderAsSubject()
    {
        var ex = new HttpRequestException("401 Unauthorized: {}", null, HttpStatusCode.Unauthorized);

        var message = RewriteFailureMessage.ForException(Profile("lm-studio"), LmStudio, ex);

        Assert.Equal("LM Studio rejected the API key. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void MissingApiKeyNamesTheProvider()
    {
        var ex = new InvalidOperationException(ClaudeEngine.MissingApiKeyMessage);

        var message = RewriteFailureMessage.ForException(Profile(), Claude, ex);

        Assert.Equal("Provider \"Claude\" has no API key. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void AnyOtherFailureGivesTheProviderAndTheExceptionOnOneLine()
    {
        var ex = new InvalidOperationException("Claude declined the request\r\n(refusal)");

        var message = RewriteFailureMessage.ForException(Profile(), Claude, ex);

        Assert.Equal("Rewriter unavailable", message.Title);
        Assert.Equal(
            "Claude failed: Claude declined the request (refusal). Raw transcript copied.",
            message.Body);
    }

    [Fact]
    public void AnyOtherFailureTrimsALongExceptionMessage()
    {
        var ex = new InvalidOperationException(new string('x', 200));

        var message = RewriteFailureMessage.ForException(Profile("ollama"), Ollama, ex);

        Assert.Equal($"Ollama failed: {new string('x', 59)}… Raw transcript copied.", message.Body);
    }

    [Fact]
    public void UnknownProviderFallsBackToAGenericSubject()
    {
        var message = RewriteFailureMessage.ForException(
            Profile(), provider: null, new InvalidOperationException("boom"));

        Assert.Equal("The provider failed: boom. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void UnnamedProviderIsNamedByItsId()
    {
        var provider = new LlmProvider { Id = "a1b2c3", Name = "", Kind = LlmProviderKind.OpenAiCompatible };

        var message = RewriteFailureMessage.ForException(
            Profile("a1b2c3"), provider, new InvalidOperationException("boom"));

        Assert.Equal("a1b2c3 failed: boom. Raw transcript copied.", message.Body);
    }

    [Fact]
    public void MissingProviderSaysSo()
    {
        var message = RewriteFailureMessage.ForUnresolvedProvider(Profile("gone"), provider: null);

        Assert.Equal("Rewriter not configured", message.Title);
        Assert.Equal(
            "Profile \"Lissage\" points at a provider that is missing. Raw transcript copied.",
            message.Body);
    }

    [Fact]
    public void DisabledProviderNamesTheProviderAndTheProfile()
    {
        var disabled = new LlmProvider
        {
            Id = "ollama", Name = "Ollama", Kind = LlmProviderKind.Ollama, Enabled = false,
        };

        var message = RewriteFailureMessage.ForUnresolvedProvider(Profile("ollama"), disabled);

        Assert.Equal(
            "Provider \"Ollama\" is disabled. Raw transcript copied.",
            message.Body);
    }

    [Fact]
    public void ProfileWithoutProviderSaysSo()
    {
        var message = RewriteFailureMessage.ForUnresolvedProvider(Profile(providerId: ""), provider: null);

        Assert.Equal("Profile \"Lissage\" has no provider. Raw transcript copied.", message.Body);
    }

    // The shape ClaudeEngine and OpenAiCompatibleEngine throw for a refusal:
    // the status on an HttpRequestException, the SDK's exception (here a
    // stand-in) kept as inner.
    static HttpRequestException EngineRefusal(HttpStatusCode status) =>
        new($"{(int)status} from the server", inner: new Exception("sdk error"), statusCode: status);
}
