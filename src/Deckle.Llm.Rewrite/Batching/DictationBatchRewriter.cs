using System.Diagnostics;
using System.Text;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// One dictation's batched rewrite: feeds the utterances to a DictationBatcher
// and sends each closed batch to the rewrite service at once, on the thread
// pool, so the model works while the speaker keeps talking and only the last
// batch is left to wait for at Stop.
//
// Threads: the transcription consumer calls Push, the engine's worker calls
// Complete and Assemble, Snapshot may come from any thread. One lock guards
// the batcher and the batch list; Assemble releases it before waiting, so a
// slow rewrite never blocks a Push.
//
// A batch whose rewrite fails or comes back blank keeps its raw text. Losing
// a stretch of the dictation would be worse than keeping it unrewritten, and
// the rest of the take still gets its rewrites; FallbackCount tells the take
// how many seams are raw.
//
// The provider's prompt cache: every batch sends the profile's whole prompt
// for a few sentences of text, so the batches ask the provider to keep it
// (IRewriteService.RewriteBatch). A cached prompt can only be read by a
// request that leaves after the one that wrote it has been taken in: batches
// sent together all pay the write, a quarter above the plain price, and none
// reads (measured on 2026-11-11 with the benchmark's cache-probe: 13 to 17 %
// dearer than no cache at all, against a third cheaper one after the other).
// So the first batch leaves alone, and the batches that close while it is
// still out wait for its answer before they leave. While the person speaks
// that wait is hidden: batches close forty seconds apart and only a catch-up
// burst ever queues. The batch flushed at Stop is the one the person waits
// for, so it never queues, whatever that costs.
public sealed class DictationBatchRewriter
{
    readonly IRewriteService _service;
    readonly RewriteProfile _profile;
    readonly DictationBatcher _batcher;
    readonly object _gate = new();
    readonly List<BatchSlot> _batches = new();

    // The first batch sent with the prompt cached, the one that writes it.
    // Guarded by _gate.
    Task? _cacheWriter;

    public DictationBatchRewriter(IRewriteService service, RewriteProfile profile, int thresholdChars)
    {
        _service = service;
        _profile = profile;
        _batcher = new DictationBatcher(thresholdChars);
    }

    // Batches closed so far, the one Complete flushed included.
    public int BatchCount
    {
        get { lock (_gate) return _batches.Count; }
    }

    // Raised on the thread-pool thread of each batch once its rewrite has
    // returned, rewritten or fallen back, after Snapshot already reflects it.
    public event Action? BatchReturned;

    public void Push(string text, bool endedOnSilence)
    {
        lock (_gate)
        {
            DictationBatch? batch = _batcher.Push(text, endedOnSilence);
            if (batch is not null) Start(batch, flushed: false);
        }
    }

    // Flushes the open batch and starts its rewrite. Calling it again with
    // nothing pushed in between finds nothing to flush, which makes it
    // idempotent. A Push after Complete is not lost: it opens a new batch
    // that the next Complete (Assemble calls one) flushes.
    public void Complete()
    {
        lock (_gate)
        {
            DictationBatch? batch = _batcher.Flush();
            if (batch is not null) Start(batch, flushed: true);
        }
    }

    // The take as it would be delivered right now: every closed batch in
    // order, rewritten once its rewrite has returned with text and raw until
    // then (or for good, when it fell back), then the open text still with the
    // batcher. Same join rule as Assemble, whose text is this snapshot's final
    // value. Never blocks.
    public string Snapshot()
    {
        BatchSlot[] batches;
        string open;
        lock (_gate)
        {
            batches = _batches.ToArray();
            open = _batcher.OpenText;
        }

        var text = new StringBuilder();
        DictationBatch? previous = null;
        foreach (BatchSlot slot in batches)
        {
            if (previous is not null) text.Append(previous.EndsOnPause ? "\n\n" : " ");
            text.Append(slot.RewrittenText ?? slot.Batch.Text);
            previous = slot.Batch;
        }

        if (open.Length > 0)
        {
            if (previous is not null) text.Append(previous.EndsOnPause ? "\n\n" : " ");
            text.Append(open);
        }

        return text.ToString();
    }

    // Blocks until every rewrite has returned, then joins the batches in
    // order: a paragraph break after a batch that ended on a pause, a space
    // otherwise. Call it from a background thread.
    public DictationBatchResult Assemble()
    {
        Complete();

        BatchSlot[] batches;
        lock (_gate) batches = _batches.ToArray();

        var text = new StringBuilder();
        int fallbacks = 0;
        long llmMs = 0, loadMs = 0, promptEvalMs = 0, evalMs = 0;
        int promptTokens = 0, evalTokens = 0, cacheWriteTokens = 0, cacheReadTokens = 0;
        DictationBatch? previous = null;

        foreach (BatchSlot slot in batches)
        {
            DictationBatch batch = slot.Batch;
            BatchOutcome outcome;
            try
            {
                outcome = slot.Rewrite.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // RewriteOne catches everything itself; this guards the task
                // machinery, not the service. RewriteOne never logged this
                // batch's end, so the fallback is logged here.
                DeckleLlmSource.Log.BatchRewriteFellBack(
                    batch.Index, "task_faulted", ex.GetType().Name, ex.Message);
                outcome = new BatchOutcome(null, 0, default);
            }

            if (previous is not null) text.Append(previous.EndsOnPause ? "\n\n" : " ");
            if (outcome.Text is null)
            {
                text.Append(batch.Text);
                fallbacks++;
            }
            else
            {
                text.Append(outcome.Text);
            }

            llmMs        += outcome.ElapsedMs;
            loadMs       += outcome.Result.LoadMs;
            promptEvalMs += outcome.Result.PromptEvalMs;
            evalMs       += outcome.Result.EvalMs;
            promptTokens += outcome.Result.PromptTokens;
            evalTokens   += outcome.Result.EvalTokens;
            cacheWriteTokens += outcome.Result.CacheWriteTokens;
            cacheReadTokens  += outcome.Result.CacheReadTokens;
            previous = batch;
        }

        return new DictationBatchResult(
            text.ToString(), batches.Length, fallbacks,
            llmMs, loadMs, promptEvalMs, evalMs, promptTokens, evalTokens,
            cacheWriteTokens, cacheReadTokens);
    }

    // Called under _gate, so batches enter the list in Index order. The slot
    // is listed before its task starts, so a Snapshot taken from BatchReturned
    // always sees the batch the event is about.
    //
    // flushed: the batch was closed by Complete, at Stop, and not by the
    // speech going on. See the class comment for what it changes.
    void Start(DictationBatch batch, bool flushed)
    {
        var slot = new BatchSlot(batch);

        // A take that ends as one batch has no second request to read the
        // prompt back: caching it would only pay the write.
        bool cached = !(flushed && _batches.Count == 0);
        _batches.Add(slot);

        if (!flushed && _cacheWriter is { } writer)
        {
            // Runs whatever the writer's outcome: a failed first batch must
            // not hold the others back, and the next one then writes the cache.
            slot.Rewrite = writer.ContinueWith(
                _ => RewriteOne(slot, cached),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }

        slot.Rewrite = Task.Run(() => RewriteOne(slot, cached));
        if (cached) _cacheWriter ??= slot.Rewrite;
    }

    BatchOutcome RewriteOne(BatchSlot slot, bool cached)
    {
        DictationBatch batch = slot.Batch;
        // Logged here, where the batch really leaves: one that waited for the
        // cache writer's answer would otherwise read as sent while it queued.
        DeckleLlmSource.Log.BatchRewriteSent();
        DeckleLlmSource.Log.BatchRewriteSentDetail(batch.Index, batch.Text.Length, _profile.Name);
        var sw = Stopwatch.StartNew();
        GenerationResult result = default;
        Exception? thrown = null;
        try
        {
            result = cached
                ? _service.RewriteBatch(batch.Text, _profile)
                : _service.Rewrite(batch.Text, _profile);
        }
        catch (Exception ex)
        {
            // RewriteService catches and reports its own failures; another
            // implementation may not, and a throw must still end as a fallback.
            // The fallback line below carries its cause.
            thrown = ex;
        }
        sw.Stop();

        string? rewritten = string.IsNullOrWhiteSpace(result.Text) ? null : result.Text.Trim();
        if (rewritten is null)
        {
            string reason = thrown is not null ? "exception"
                          : result.Text is null ? "no_result"
                          : "blank";
            DeckleLlmSource.Log.BatchRewriteFellBack(
                batch.Index, reason, thrown?.GetType().Name ?? "", thrown?.Message ?? "");
        }
        else
        {
            DeckleLlmSource.Log.BatchRewriteReturned();
            DeckleLlmSource.Log.BatchRewriteReturnedDetail(
                batch.Index, sw.ElapsedMilliseconds, batch.Text.Length, rewritten.Length);
        }

        // Published before the event, so a Snapshot taken from the handler
        // reads the rewrite; the task itself completes only after this returns.
        slot.RewrittenText = rewritten;
        BatchReturned?.Invoke();
        return new BatchOutcome(rewritten, sw.ElapsedMilliseconds, result);
    }

    // One closed batch and the rewrite in flight for it. RewrittenText is
    // null while the rewrite runs and stays null when it falls back: either
    // way the batch reads raw.
    sealed class BatchSlot(DictationBatch batch)
    {
        public DictationBatch Batch { get; } = batch;
        public Task<BatchOutcome> Rewrite { get; set; } = Task.FromResult(new BatchOutcome(null, 0, default));
        public volatile string? RewrittenText;
    }

    // Text is the trimmed rewrite, or null when the batch keeps its raw text.
    readonly record struct BatchOutcome(string? Text, long ElapsedMs, GenerationResult Result);
}
