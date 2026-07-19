namespace Deckle.Llm.Rewrite;

// Owns the asynchronous offer lifecycle. Every input mutation advances the
// revision and cancels the in-flight request, so a result can surface only for
// the exact caret state that requested it.
public sealed class ParagraphRewriteCoordinator : IDisposable
{
    private readonly IRewriteService _service;
    private readonly Func<string> _endpoint;
    private readonly object _lock = new();

    private CancellationTokenSource? _request;
    private long _revision;
    private bool _disposed;

    public ParagraphRewriteCoordinator(IRewriteService service, Func<string> endpoint)
    {
        _service = service;
        _endpoint = endpoint;
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
        try
        {
            RewriteResult result = _service.RewriteParagraph(paragraph, _endpoint(), token);
            if (result.Text is null) return;

            DiffGateVerdict verdict = RewriteDiffGate.Evaluate(paragraph, result.Text);
            if (!verdict.Accepted || verdict.IsIdentity) return;

            lock (_lock)
            {
                if (_disposed || token.IsCancellationRequested || revision != _revision) return;
                if (!ReferenceEquals(_request, request)) return;
                _request = null;
            }

            OfferReady?.Invoke(new ParagraphRewriteOffer(revision, paragraph, result.Text, verdict));
        }
        finally
        {
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
