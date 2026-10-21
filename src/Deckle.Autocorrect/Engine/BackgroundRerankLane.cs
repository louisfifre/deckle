using System.Collections.Concurrent;
using System.Threading;
using Deckle.Input;

namespace Deckle.Autocorrect;

// The production IRerankLane: one long-lived background thread owns the
// reranker (and thus the ONNX session), so inference never touches the input
// thread. Requests cross in through a capacity-1, drop-oldest queue — only the
// freshest slot matters, a backlog can never form. A request already running is
// not cancelled: a stale verdict still occupies the worker and a fresh request
// waits for it, so the epoch check protects what gets applied, not latency.
// Verdicts cross back as a ConcurrentQueue the input thread drains: the worker
// enqueues a result and posts a drain message to the host pump (RequestDrain),
// and the host raises DrainRequested on the input thread, where ResultSink
// applies it against live engine state.
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

    // A failing judge fails every sentence the same way, so the failures form
    // one incident: one Warning when it opens, one Informational when a request
    // goes through again, each raw failure as gated Verbose detail. The first
    // failure opens it, because a scoring failure is never part of normal
    // operation. Worker thread only.
    private const int FailureIncidentThreshold = 1;
    private int _consecutiveFailures;
    private bool _failureIncidentOpen;

    // Superseded requests are the lane's designed behavior, not an error; their
    // count at shutdown says how much typing outran the judge.
    private int _droppedRequests;

    // Written by the worker before it exits, read by Dispose after Join.
    private string _workerOutcome = "completed";

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
        DeckleAutocorrectSource.Log.RerankLaneStarted(
            _reranker is IWholeSentenceReranker, _caretTextReader is not null);
    }

    // Input thread; never blocks.
    public void Submit(RerankRequest request)
    {
        if (_disposed)
            return;
        while (_queue.TryTake(out _))
            Interlocked.Increment(ref _droppedRequests);
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
                Exception? drainFailure = null;
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
                catch (Exception ex)
                {
                    // A scoring failure abstains, never crashes the lane. The
                    // exception rides on the outcome so the cause reaches the log.
                    outcome = RerankOutcome.Abstained(RerankOutcome.AbstainReasons.Error)
                        with { Failure = ex };
                }

                int slotIndex = req is HistoricalSlotRerankRequest slotRequest
                    ? slotRequest.SlotIndex
                    : -1;
                _completed.Enqueue(new RerankResult(slotIndex, req.Epoch, outcome));
                if (!_disposed)
                {
                    try
                    {
                        _host.RequestDrain();
                    }
                    catch (Exception ex)
                    {
                        // A lost drain post strands this verdict until the next
                        // one drains the queue; the lane itself keeps serving.
                        drainFailure = ex;
                    }
                }

                if (outcome.Failure is Exception inferenceFailure)
                    TrackFailure(DeckleAutocorrectSource.RerankFailureSources.Inference, inferenceFailure);
                else if (drainFailure is not null)
                    TrackFailure(DeckleAutocorrectSource.RerankFailureSources.Delivery, drainFailure);
                else
                    TrackSuccess();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled on Dispose — fall through and exit.
            _workerOutcome = "cancelled";
        }
        catch (Exception ex)
        {
            // Without this the fault would kill the process from a background
            // thread; the lane goes quiet instead and the engine keeps its
            // deterministic corrections.
            _workerOutcome = "failed";
            DeckleAutocorrectSource.Log.RerankWorkerFaulted(
                ex.GetType().FullName ?? ex.GetType().Name, ex.Message, ex.HResult);
        }
    }

    private void TrackFailure(string source, Exception failure)
    {
        _consecutiveFailures++;
        string exType = failure.GetType().FullName ?? failure.GetType().Name;
        DeckleAutocorrectSource.Log.RerankFailureDetail(
            source, exType, failure.Message, failure.HResult, _consecutiveFailures);
        if (!_failureIncidentOpen && _consecutiveFailures >= FailureIncidentThreshold)
        {
            _failureIncidentOpen = true;
            DeckleAutocorrectSource.Log.RerankFailing(
                source, exType, failure.Message, failure.HResult, _consecutiveFailures);
        }
    }

    private void TrackSuccess()
    {
        if (_failureIncidentOpen)
            DeckleAutocorrectSource.Log.RerankRecovered(_consecutiveFailures);
        _failureIncidentOpen = false;
        _consecutiveFailures = 0;
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
        long joinStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _worker.Join();
        DeckleAutocorrectSource.Log.RerankLaneStopped(
            _workerOutcome,
            (long)System.Diagnostics.Stopwatch.GetElapsedTime(joinStarted).TotalMilliseconds,
            Volatile.Read(ref _droppedRequests));
        _cts.Dispose();
        _queue.Dispose();
        (_reranker as IDisposable)?.Dispose();
    }
}
