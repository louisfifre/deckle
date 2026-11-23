using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

/// <summary>Asks whether the paragraph rewrite can run on this PC. Never
/// throws except for the caller's own cancellation.</summary>
public interface IParagraphRewriteProbe
{
    Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken);
}

// The local probe: one GET of Ollama's model list on the provider's address,
// nothing is generated and nothing leaves the machine.
public sealed class OllamaParagraphRewriteProbe : IParagraphRewriteProbe
{
    // Short on purpose: a local answer comes in milliseconds, and a probe
    // that hangs must not hold the next one.
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public delegate Task<IReadOnlyList<string>> ListModelNames(string endpoint, CancellationToken cancellationToken);

    readonly Func<IReadOnlyList<LlmProvider>> _providers;
    readonly ListModelNames _list;

    public OllamaParagraphRewriteProbe()
        : this(() => LlmSettingsService.Instance.Current.Providers, ListFromOllama) { }

    public OllamaParagraphRewriteProbe(Func<IReadOnlyList<LlmProvider>> providers, ListModelNames list)
    {
        _providers = providers;
        _list = list;
    }

    /// <summary>The provider the paragraph rewrite sends to, when it can be
    /// used at all.</summary>
    public static LlmProvider? FindProvider(IReadOnlyList<LlmProvider> providers)
    {
        foreach (LlmProvider provider in providers)
        {
            if (provider.Id == ParagraphRewrite.ProviderId)
                return provider.Enabled && provider.Kind == LlmProviderKind.Ollama ? provider : null;
        }
        return null;
    }

    public async Task<ParagraphRewriteAvailability> ProbeAsync(CancellationToken cancellationToken)
    {
        LlmProvider? provider = FindProvider(_providers());
        if (provider is null) return ParagraphRewriteAvailability.ProviderOff;

        IReadOnlyList<string> names;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            names = await _list(provider.Endpoint, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Refused, timed out, not HTTP: all the same answer for a person,
            // Ollama is not there. The state change is logged by the monitor.
            return ParagraphRewriteAvailability.OllamaUnreachable;
        }

        return Holds(names) ? ParagraphRewriteAvailability.Available : ParagraphRewriteAvailability.ModelMissing;
    }

    static bool Holds(IReadOnlyList<string> names)
    {
        foreach (string name in names)
        {
            if (string.Equals(name, ParagraphRewrite.Model, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static async Task<IReadOnlyList<string>> ListFromOllama(string endpoint, CancellationToken cancellationToken)
    {
        List<OllamaModel> models = await new OllamaService(() => endpoint)
            .ListModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.ConvertAll(m => m.Name);
    }
}
