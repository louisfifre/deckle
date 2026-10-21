using System.Net.Http;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Deckle.Llm;

// ─── Claude engine ───────────────────────────────────────────────────────────
//
// Anthropic's Messages API through the official C# SDK. The key comes from
// the secret vault on each request (the SDK client is rebuilt only when the
// key changes), so saving a key in Settings is enough. Sampling hints
// (temperature, top_p) are dropped: the current Claude models reject
// non-default values, and a rewrite wants the model's default anyway.
// Thinking stays at the SDK default; effort is held low because a rewrite is
// a transcription task, not a reasoning one, and low effort keeps the answer
// close to the input and the cost near the floor.
//
// Transport only: the provider lookup, the hard cap and the human-facing
// failure outcomes live above the seam in the consumer. This engine throws
// and lets the caller decide.

public sealed class ClaudeEngine : IGenerationEngine
{
    // A rewrite is never longer than its input, but the cap must still leave
    // room for a long dictation: 16 K output tokens covers an hour of speech.
    const int MaxOutputTokens = 16_000;

    // Public so the consumer that words the failure for the user can tell a
    // missing key from any other InvalidOperationException by exact match.
    public const string MissingApiKeyMessage = "No API key is stored for this Claude provider.";

    // Public for the same reason: the failure class logged above the seam
    // (reason=refused) is read by exact match, not by parsing the message.
    public const string RefusalMessage = "Claude declined the request (refusal).";

    private readonly Func<string?> _apiKey;
    private readonly object _lock = new();
    private AnthropicClient? _client;
    private string? _clientKey;

    public ClaudeEngine(Func<string?> apiKey) => _apiKey = apiKey;

    public GenerationResult Generate(GenerationRequest request, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string? key = _apiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(MissingApiKeyMessage);

        AnthropicClient client = ClientFor(key);

        // RewriteService opens and closes the rewrite milestone (before the
        // key check above, so a missing key still has its start); the engine
        // adds only what its transport sees.
        DeckleLlmSource.Log.RewriteStartedDetail(
            request.UserText.Length, request.Model, request.Label, "claude", "effort=low");

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = MaxOutputTokens,
            System = request.SystemPrompt,
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Messages = [new() { Role = Role.User, Content = request.UserText }],
        };

        Message response;
        try
        {
            response = client.Messages.Create(parameters, cancellationToken: cancellationToken)
                .GetAwaiter().GetResult();
        }
        catch (AnthropicApiException ex)
        {
            // The seam's callers read a refusal's status from
            // HttpRequestException.StatusCode, whatever the transport, so the
            // SDK's own exception type never leaks past this engine.
            // AnthropicIOException passes through: it already wraps the
            // HttpRequestException and SocketException a caller looks for.
            throw new HttpRequestException(ex.Message, inner: ex, statusCode: ex.StatusCode);
        }

        if (response.StopReason == "refusal")
            throw new InvalidOperationException(RefusalMessage);

        var text = new System.Text.StringBuilder();
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? textBlock))
                text.Append(textBlock.Text);
        }

        int promptTokens = (int)response.Usage.InputTokens;
        int evalTokens   = (int)response.Usage.OutputTokens;

        sw.Stop();
        string trimmed = text.ToString().Trim();
        // stop_reason (end_turn, max_tokens, ...) is formatted the way the
        // former metrics line did, through the SDK type's own ToString. The
        // Messages API reports no load or prompt-evaluation timing.
        DeckleLlmSource.Log.RewriteResponseDetail(
            sw.ElapsedMilliseconds, request.UserText.Length, trimmed.Length, request.Label,
            0, 0, sw.ElapsedMilliseconds, promptTokens, evalTokens, $"{response.StopReason}");

        return new GenerationResult(
            Text:         trimmed,
            TotalMs:      sw.ElapsedMilliseconds,
            LoadMs:       0,
            PromptEvalMs: 0,
            EvalMs:       sw.ElapsedMilliseconds,
            PromptTokens: promptTokens,
            EvalTokens:   evalTokens);
    }

    private AnthropicClient ClientFor(string key)
    {
        lock (_lock)
        {
            if (_client is null || _clientKey != key)
            {
                _client = new AnthropicClient { ApiKey = key };
                _clientKey = key;
            }
            return _client;
        }
    }
}
