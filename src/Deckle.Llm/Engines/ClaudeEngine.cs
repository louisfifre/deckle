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
// Without a reasoning level, thinking stays at the SDK default (adaptive) and
// effort is held low, because a rewrite is a transcription task, not a
// reasoning one, and low effort keeps the answer close to the input and the
// cost near the floor. A level, when the request names one, is mapped by
// ReasoningFor below.
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
        ClaudeReasoning reasoning = ReasoningFor(request.Model, request.Reasoning);
        DeckleLlmSource.Log.RewriteStartedDetail(
            request.UserText.Length, request.Model, request.Label, "claude", reasoning.Detail);

        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = MaxOutputTokens,
            System = request.SystemPrompt,
            OutputConfig = new OutputConfig { Effort = reasoning.Effort },
            Messages = [new() { Role = Role.User, Content = request.UserText }],
        };
        // Left unset unless thinking must be turned off, so a request without
        // a level stays byte for byte what it was.
        if (reasoning.Thinking is { } thinking)
            parameters = parameters with { Thinking = thinking };
        // Set only when asked, so the app's request stays byte for byte what
        // it was. The top-level marker caches up to the last block: the
        // system prompt and the text together.
        if (request.CachePrompt)
            parameters = parameters with { CacheControl = new CacheControlEphemeral() };

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

        int promptTokens     = (int)response.Usage.InputTokens;
        int evalTokens       = (int)response.Usage.OutputTokens;
        int cacheWriteTokens = (int)(response.Usage.CacheCreationInputTokens ?? 0);
        int cacheReadTokens  = (int)(response.Usage.CacheReadInputTokens ?? 0);

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
            EvalTokens:   evalTokens,
            CacheWriteTokens: cacheWriteTokens,
            CacheReadTokens:  cacheReadTokens);
    }

    /// <summary>What the Messages API is sent for a reasoning level:
    /// <see cref="ClaudeReasoning.Thinking"/> null leaves thinking adaptive,
    /// the default of every current model.</summary>
    internal readonly record struct ClaudeReasoning(Effort Effort, ThinkingConfigParam? Thinking, string Detail);

    // Turning thinking off is not one switch across the current models
    // (claude-api reference, 2026-10): Claude Haiku 5.5 and the models before
    // the 5.5 line take thinking "disabled" at effort high or below; Claude
    // Sonnet 5.5 rejects "disabled" with a 400 and turns it off with
    // "between_tools", which on a request without tools means no thinking at
    // all; Claude Opus 5.5 and the Fable and Mythos lines think whatever is
    // sent. On those, Off is dropped on purpose to the lowest effort, which
    // is the closest the model allows, rather than failing the rewrite.
    static readonly string[] AlwaysThinkingModels = ["claude-opus-5-5", "claude-fable-", "claude-mythos-"];
    const string BetweenToolsModel = "claude-sonnet-5-5";

    internal static ClaudeReasoning ReasoningFor(string model, ReasoningLevel? level) => level switch
    {
        null                  => new(Effort.Low, null, "effort=low"),
        ReasoningLevel.Low    => new(Effort.Low, null, "effort=low reasoning=low"),
        ReasoningLevel.Medium => new(Effort.Medium, null, "effort=medium reasoning=medium"),
        ReasoningLevel.High   => new(Effort.High, null, "effort=high reasoning=high"),
        ReasoningLevel.Off when AlwaysThinkingModels.Any(m => model.StartsWith(m, StringComparison.OrdinalIgnoreCase))
                              => new(Effort.Low, null, "effort=low reasoning=off_unsupported"),
        ReasoningLevel.Off when model.StartsWith(BetweenToolsModel, StringComparison.OrdinalIgnoreCase)
                              => new(Effort.Low, new ThinkingConfigBetweenTools(), "effort=low reasoning=off thinking=between_tools"),
        ReasoningLevel.Off    => new(Effort.Low, new ThinkingConfigDisabled(), "effort=low reasoning=off thinking=disabled"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

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
