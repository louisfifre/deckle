using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// Holds the last answer of the probe and says when it changed. The App
// reconciles the keyboard host on it and the Rewriting page shows its notice
// from it; both read the one answer, so they cannot disagree.
//
// A state change is logged once, however many probes confirm it: an Ollama
// that is simply not installed answers the same every thirty seconds.
public sealed class ParagraphRewriteAvailabilityMonitor
{
    static readonly Lazy<ParagraphRewriteAvailabilityMonitor> _instance =
        new(() => new ParagraphRewriteAvailabilityMonitor(new OllamaParagraphRewriteProbe()));

    public static ParagraphRewriteAvailabilityMonitor Instance => _instance.Value;

    readonly IParagraphRewriteProbe _probe;
    int _state = (int)ParagraphRewriteAvailability.Unknown;
    int _generation;

    public ParagraphRewriteAvailabilityMonitor(IParagraphRewriteProbe probe) => _probe = probe;

    public ParagraphRewriteAvailability Current =>
        (ParagraphRewriteAvailability)Volatile.Read(ref _state);

    /// <summary>Raised on the probing thread, only when the state changed.</summary>
    public event Action? Changed;

    /// <summary>Probes once. Only the latest probe may set the state: an older
    /// answer arriving last is dropped.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        int generation = Interlocked.Increment(ref _generation);

        ParagraphRewriteAvailability result;
        try
        {
            result = await _probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            result = ParagraphRewriteAvailability.OllamaUnreachable;
        }

        if (generation != Volatile.Read(ref _generation)) return;

        var previous = (ParagraphRewriteAvailability)Interlocked.Exchange(ref _state, (int)result);
        if (previous == result) return;

        DeckleLlmSource.Log.ParagraphRewriteAvailabilityChanged(result.Name(), previous.Name());
        Changed?.Invoke();
    }
}
