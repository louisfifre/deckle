using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// Owns the asynchronous offer lifecycle. Every input mutation advances the
// revision and cancels the in-flight request, so a result can surface only for
// the exact caret state that requested it.
public sealed class ParagraphRewriteCoordinator : IDisposable
{
    private readonly IRewriteService _service;
    private readonly object _lock = new();

    private CancellationTokenSource? _request;
    private long _revision;
    private bool _disposed;

    public ParagraphRewriteCoordinator(IRewriteService service)
    {
        _service = service;
    }

    public event Action<ParagraphRewriteOffer>? OfferReady;
    public event Action? OfferInvalidated;

    public void Request(string paragraph)
    {
        CancellationTokenSource request;
        long revision;
        lock (_lock)
        {
            if (_disposed) return;
            CancelRequest();
            revision = ++_revision;
            request = new CancellationTokenSource(ParagraphRewrite.Deadline);
            _request = request;
        }

        CancellationToken token = request.Token;
        _ = Task.Run(() => Generate(paragraph, revision, request, token));
    }

    public bool IsCurrent(long revision)
    {
        lock (_lock)
            return !_disposed && revision == _revision;
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _revision++;
            CancelRequest();
        }
        OfferInvalidated?.Invoke();
    }

    private void Generate(
        string paragraph,
        long revision,
        CancellationTokenSource request,
        CancellationToken token)
    {
        // One settled line per request, whatever became of it: without it a
        // paragraph that never got an offer leaves no trace of why. Stays
        // "failed" unless a branch below says otherwise.
        string outcome = "failed";
        int edits = 0, rejectedEdits = 0;
        try
        {
            GenerationResult result = _service.RewriteParagraph(paragraph, token);
            if (result.Text is null)
            {
                // The service already logged a failure or its cancellation;
                // this only says which one the offer saw.
                outcome = token.IsCancellationRequested ? "cancelled" : "no_result";
                return;
            }

            DiffGateVerdict verdict = RewriteDiffGate.Evaluate(paragraph, result.Text);
            edits = verdict.Edits.Count;
            foreach (DiffEdit edit in verdict.Edits)
            {
                if (!edit.IsAllowed) rejectedEdits++;
            }
            if (!verdict.Accepted) { outcome = "gate_rejected"; return; }
            if (verdict.IsIdentity) { outcome = "identity"; return; }

            lock (_lock)
            {
                if (_disposed || token.IsCancellationRequested || revision != _revision
                    || !ReferenceEquals(_request, request))
                {
                    outcome = "stale";
                    return;
                }
                _request = null;
            }

            OfferReady?.Invoke(new ParagraphRewriteOffer(revision, paragraph, result.Text, verdict));
            outcome = "offered";
        }
        catch (Exception ex)
        {
            // This runs as a fire-and-forget Task.Run: an exception here (the
            // gate, an OfferReady handler) would otherwise surface only as an
            // unobserved task exception, long after and without context. The
            // offer is optional, so it is dropped and its cause logged.
            DeckleLlmSource.Log.ParagraphRewriteFailed(ex.GetType().Name, ex.Message);
        }
        finally
        {
            DeckleLlmSource.Log.ParagraphOfferSettled(outcome, revision, edits, rejectedEdits);
            lock (_lock)
            {
                if (ReferenceEquals(_request, request))
                    _request = null;
            }
            request.Dispose();
        }
    }

    private void CancelRequest()
    {
        CancellationTokenSource? request = _request;
        _request = null;
        if (request is null) return;
        request.Cancel();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            CancelRequest();
        }
    }
}
