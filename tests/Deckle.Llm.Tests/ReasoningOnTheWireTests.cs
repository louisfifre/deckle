using System.Net;
using System.Text;
using System.Text.Json;
using Deckle.TestSupport;
using Xunit;

namespace Deckle.Llm.Tests;

// The body an HTTP engine sends for a reasoning level, read by a loopback
// server standing in for the provider at the endpoint the engine is bound to.
[Trait("Category", "integration")]
public sealed class ReasoningOnTheWireTests
{
    const string ChatAnswer =
        """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";
    const string GenerateAnswer = """{"response":"ok","done_reason":"stop"}""";

    static GenerationRequest Request(ReasoningLevel? level) =>
        new(Model: "m", SystemPrompt: "s", UserText: "t", Label: "test", Reasoning: level);

    [Theory]
    [InlineData(ReasoningLevel.Off, "none")]
    [InlineData(ReasoningLevel.Low, "low")]
    [InlineData(ReasoningLevel.Medium, "medium")]
    [InlineData(ReasoningLevel.High, "high")]
    public void ChatCompletionsSendsTheLevelAsReasoningEffort(ReasoningLevel level, string expected)
    {
        using var body = Capture(ChatAnswer, prefix =>
            new OpenAiCompatibleEngine(prefix, () => null).Generate(Request(level), CancellationToken.None));

        Assert.Equal(expected, body.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public void ChatCompletionsSendsNoReasoningEffortWithoutALevel()
    {
        using var body = Capture(ChatAnswer, prefix =>
            new OpenAiCompatibleEngine(prefix, () => null).Generate(Request(null), CancellationToken.None));

        Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public void OllamaRawModeDropsTheLevel()
    {
        using var body = Capture(GenerateAnswer, prefix =>
            new OllamaEngine(prefix + "api/generate").Generate(Request(ReasoningLevel.High), CancellationToken.None));

        Assert.False(body.RootElement.TryGetProperty("think", out _));
        Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    // Runs the engine against a loopback server that answers its one request
    // with `answer`, and returns the JSON body the engine sent.
    static JsonDocument Capture(string answer, Action<string> callEngine)
    {
        using var lease = LoopbackHttpListenerLease.Start();
        var served = Task.Run(() =>
        {
            HttpListenerContext context = lease.Listener.GetContext();
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            string sent = reader.ReadToEnd();
            byte[] bytes = Encoding.UTF8.GetBytes(answer);
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
            return sent;
        });

        callEngine(lease.Prefix);
        return JsonDocument.Parse(served.GetAwaiter().GetResult());
    }
}
