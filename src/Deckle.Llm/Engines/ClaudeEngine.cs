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
// ReasoningFor below; Low sends exactly the request without one.
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

        MessageCreateParams parameters = BuildParameters(request, reasoning);

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
            0, 0, sw.ElapsedMilliseconds, promptTokens, evalTokens, $"{response.StopReason}",
            cacheWriteTokens, cacheReadTokens);

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

    // The request as the Messages API receives it. Apart from Generate so a
    // test can read where the cache marker sits without a network call.
    internal static MessageCreateParams BuildParameters(GenerationRequest request, ClaudeReasoning reasoning)
    {
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

        // The cache keeps everything up to its marker and matches the next
        // request from its first byte, so the marker goes where the part that
        // repeats ends. Without a scope nothing is added and the request
        // stays byte for byte what it was.
        return request.Cache switch
        {
            // On the prompt's own block: the text that follows is new on
            // every call, so it is neither written to the cache nor needed
            // to read the prompt back.
            PromptCacheScope.SystemPrompt => parameters with
            {
                System = new List<TextBlockParam>
                {
                    new() { Text = request.SystemPrompt, CacheControl = new CacheControlEphemeral() },
                },
            },
            // The top-level marker caches up to the last block: the system
            // prompt and the text together.
            PromptCacheScope.WholeRequest => parameters with { CacheControl = new CacheControlEphemeral() },
            _ => parameters,
        };
    }

    /// <summary>What the Messages API is sent for a reasoning level:
    /// <see cref="ClaudeReasoning.Thinking"/> null leaves thinking adaptive,
    /// the default of every current model.</summary>
    internal readonly record struct ClaudeReasoning(Effort Effort, ThinkingConfigParam? Thinking, string Detail);

    // Turning thinking off is not one switch across the current models
    // (claude-api reference, 2026-10): Claude Haiku 5.5 takes thinking
    // "disabled"; Claude Sonnet 5.5 rejects "disabled" with a 400 and turns it
    // off with "between_tools", which on a request without tools means no
    // thinking at all; Claude Opus 5.5 and the Fable and Mythos lines think
    // whatever is sent. Off is therefore offered only on the models listed
    // here, each with the field it takes, and a model this list does not know
    // yet gets Low and Medium until its rule is checked. A stored Off that
    // meets a model outside the list (the profile's model changed) is sent as
    // the lowest effort, the closest the model allows, rather than failing the
    // rewrite; the page shows it as Low for the same model.
    sealed record ThinkingOff(string Prefix, string Name, Func<ThinkingConfigParam> Build);

    static readonly ThinkingOff[] ModelsThatTurnThinkingOff =
    [
        new("claude-haiku-5-5", "disabled", () => new ThinkingConfigDisabled()),
        new("claude-sonnet-5-5", "between_tools", () => new ThinkingConfigBetweenTools()),
    ];

    /// <summary>Whether <paramref name="model"/> can be asked not to think,
    /// so the profile's list offers Off only where it is honoured.</summary>
    public static bool AcceptsOff(string? model) => OffFor(model) is not null;

    static ThinkingOff? OffFor(string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? null
            : ModelsThatTurnThinkingOff.FirstOrDefault(
                m => model.Trim().StartsWith(m.Prefix, StringComparison.OrdinalIgnoreCase));

    internal static ClaudeReasoning ReasoningFor(string model, ReasoningLevel? level) => level switch
    {
        null                  => new(Effort.Low, null, "effort=low"),
        ReasoningLevel.Low    => new(Effort.Low, null, "effort=low reasoning=low"),
        ReasoningLevel.Medium => new(Effort.Medium, null, "effort=medium reasoning=medium"),
        ReasoningLevel.Off when OffFor(model) is { } off
                              => new(Effort.Low, off.Build(), $"effort=low reasoning=off thinking={off.Name}"),
        ReasoningLevel.Off    => new(Effort.Low, null, "effort=low reasoning=off_unsupported"),
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
