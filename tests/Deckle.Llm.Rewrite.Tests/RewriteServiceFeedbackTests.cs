using System.Diagnostics.Tracing;
using System.Net.Http;
using System.Net.Sockets;
using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Rewrite.Tests;

// The overlay card is whatever UserFeedbackEmitted carries on Deckle-Llm, so
// the test reads it there, the same place HudFeedbackSink does.
[Trait("Category", "unit")]
public sealed class RewriteServiceFeedbackTests
{
    static readonly LlmProvider Ollama = new()
    {
        Id = "ollama", Name = "Ollama", Kind = LlmProviderKind.Ollama,
        Endpoint = "http://localhost:11434/api/generate",
    };

    static readonly RewriteProfile Profile = new()
    {
        Name = "Lissage", ProviderId = "ollama", Model = "mistral-small3.2",
    };

    [Fact]
    public void EngineFailureFeedbackNamesTheProviderAndItsAddress()
    {
        var refused = new HttpRequestException(
            HttpRequestError.ConnectionError,
            "No connection could be made because the target machine actively refused it.",
            new SocketException((int)SocketError.ConnectionRefused));
        var service = new RewriteService(
            new SingleEngine("ollama", new ThrowingEngine(refused)),
            id => id == Ollama.Id ? Ollama : null);
        using var listener = new TestEventListener("Deckle-Llm");

        GenerationResult result = service.Rewrite("euh bonjour", Profile);

        Assert.Null(result.Text);
        Assert.Contains(
            Feedback(listener),
            f => f.Title == "Rewriter unavailable"
              && f.Body == "Ollama is not answering at localhost:11434. Raw transcript copied.");
    }

    [Fact]
    public void DisabledProviderFeedbackNamesTheProvider()
    {
        var disabled = new LlmProvider
        {
            Id = "ollama", Name = "Ollama", Kind = LlmProviderKind.Ollama, Enabled = false,
        };
        var service = new RewriteService(new NoEngine(), id => id == disabled.Id ? disabled : null);
        using var listener = new TestEventListener("Deckle-Llm");

        service.Rewrite("euh bonjour", Profile);

        Assert.Contains(
            Feedback(listener),
            f => f.Title == "Rewriter not configured"
              && f.Body == "Provider \"Ollama\" is disabled. Raw transcript copied.");
    }

    static IEnumerable<(string? Title, string? Body)> Feedback(TestEventListener listener) =>
        listener.Events
            .Where(e => e.EventId == DeckleLlmSource.EvtUserFeedbackEmitted)
            .Select(e => (Field(e, "title"), Field(e, "body")));

    static string? Field(EventWrittenEventArgs e, string name) =>
        e.Payload![e.PayloadNames!.IndexOf(name)] as string;

    private sealed class SingleEngine(string providerId, IGenerationEngine engine) : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string id) => id == providerId ? engine : null;
    }

    private sealed class NoEngine : IGenerationEngineResolver
    {
        public IGenerationEngine? Resolve(string id) => null;
    }

    private sealed class ThrowingEngine(Exception exception) : IGenerationEngine
    {
        public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken) =>
            throw exception;
    }
}
