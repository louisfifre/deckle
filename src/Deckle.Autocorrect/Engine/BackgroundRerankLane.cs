using System.Collections.Concurrent;
using System.Threading;
using Deckle.Input;

namespace Deckle.Autocorrect;

// ── BackgroundRerankLane ─────────────────────────────────────────────────────
//
// The production IRerankLane: one long-lived background thread owns the
// CamemBERT reranker (and thus the ONNX session, CPU-bound and single-threaded by
// policy), so the ~100 ms inference never touches the input thread. Requests cross
// in through a capacity-1, drop-oldest queue — only the freshest slot matters, a
// backlog can never form. Verdicts cross back as a ConcurrentQueue the input
// thread drains: the worker enqueues a result and posts a drain message to the
// host pump (RequestDrain), and the host raises DrainRequested on the input
// thread, where ResultSink applies it against live engine state.
//
// Teardown does not trust any pump join (the keyboard host is reference-counted
// and may outlive this consumer): Dispose cancels the worker, completes the queue,
// waits for an in-flight native inference to return, then disposes the reranker.
// The wait is deliberately not timed: releasing or replacing an ONNX GenAI model
// while its worker still owns native state can terminate the whole process.
public sealed class BackgroundRerankLane : IRerankLane
{
    private readonly ISentenceReranker _reranker;
    private readonly IKeyboardInputHost _host;
    private readonly ICaretTextReader? _caretTextReader;
    private readonly BlockingCollection<RerankRequest> _queue = new(boundedCapacity: 1);
    private readonly ConcurrentQueue<RerankResult> _completed = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _worker;
    private volatile bool _disposed;

    public Action<RerankResult>? ResultSink { get; set; }

    public BackgroundRerankLane(
        ISentenceReranker reranker,
        IKeyboardInputHost host,
        ICaretTextReader? caretTextReader = null)
    {
        _reranker = reranker;
        _host = host;
        _caretTextReader = caretTextReader;
        _host.DrainRequested += OnDrainRequested;
        _worker = new Thread(WorkerLoop)
        {
            Name = "Deckle Autocorrect Rerank",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        _worker.Start();
    }

    // Input thread. Replace any queued-but-not-yet-started request with the newest
    // (drop-oldest), then enqueue — never blocks.
    public void Submit(RerankRequest request)
    {
        if (_disposed)
            return;
        while (_queue.TryTake(out _)) { }
        try { _queue.TryAdd(request); }
        catch (InvalidOperationException) { /* completed concurrently with Dispose */ }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (RerankRequest req in _queue.GetConsumingEnumerable(_cts.Token))
            {
                RerankOutcome outcome;
                try
                {
                    outcome = req switch
                    {
                        ClosedSentenceRerankRequest closed =>
                            _reranker is IWholeSentenceReranker wholeSentence
                                ? wholeSentence.RerankSentence(closed.Transaction)
                                : RerankOutcome.Abstained(
                                    RerankOutcome.AbstainReasons.WholeSentenceUnsupported),
                        HistoricalSlotRerankRequest historical => _reranker.Rerank(
                            historical.Sentence,
                            historical.SlotIndex,
                            historical.Candidates),
                        _ => RerankOutcome.Abstained(RerankOutcome.AbstainReasons.Error),
                    };
                    if (WouldRewrite(req, outcome)
                        && req.VerifiedSentence is VerifiedCaretSentence verified
                        && !VerifyRecoveredSentence(verified))
                    {
                        outcome = RerankOutcome.Abstained(
                            RerankOutcome.AbstainReasons.StaleEvidence);
                    }
                }
                catch
                {
                    // A scoring failure abstains, never crashes the lane.
                    outcome = RerankOutcome.Abstained(RerankOutcome.AbstainReasons.Error);
                }

                int slotIndex = req is HistoricalSlotRerankRequest slotRequest
                    ? slotRequest.SlotIndex
                    : -1;
                _completed.Enqueue(new RerankResult(slotIndex, req.Epoch, outcome));
                if (!_disposed)
                    _host.RequestDrain();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled on Dispose — fall through and exit.
        }
    }

    private bool VerifyRecoveredSentence(VerifiedCaretSentence verified)
    {
        return _caretTextReader is not null
            && _caretTextReader.TryReadStable(out Deckle.Core.FocusedCaretText current, out _)
            && verified.Matches(current);
    }

    private static bool WouldRewrite(RerankRequest request, RerankOutcome outcome) =>
        outcome.Chosen is not null
        && (request switch
        {
            ClosedSentenceRerankRequest closed =>
                outcome.ChosenSlotIndex is int chosenSlot
                && chosenSlot >= 0
                && chosenSlot < closed.Transaction.Words.Count
                && !string.Equals(
                    outcome.Chosen,
                    closed.Transaction.Words[chosenSlot],
                    StringComparison.Ordinal),
            HistoricalSlotRerankRequest historical =>
                historical.SlotIndex >= 0
                && historical.SlotIndex < historical.Sentence.Count
                && !string.Equals(
                    outcome.Chosen,
                    historical.Sentence[historical.SlotIndex],
                    StringComparison.Ordinal),
            _ => false,
        });

    // Input thread (raised by the host pump). Drain every queued verdict.
    private void OnDrainRequested()
    {
        if (_disposed)
            return;
        while (_completed.TryDequeue(out RerankResult result))
            ResultSink?.Invoke(result);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.DrainRequested -= OnDrainRequested;
        _cts.Cancel();
        _queue.CompleteAdding();
        _worker.Join();
        _cts.Dispose();
        _queue.Dispose();
        (_reranker as IDisposable)?.Dispose();
    }
}
