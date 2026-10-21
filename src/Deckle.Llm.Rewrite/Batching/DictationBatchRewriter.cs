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
// Complete and Assemble. One lock guards the batcher and the batch list;
// Assemble releases it before waiting, so a slow rewrite never blocks a Push.
//
// A batch whose rewrite fails or comes back blank keeps its raw text. Losing
// a stretch of the dictation would be worse than pasting it unrewritten, and
// the rest of the take still gets its rewrites; FallbackCount tells the take
// how many seams are raw.
public sealed class DictationBatchRewriter
{
    readonly IRewriteService _service;
    readonly RewriteProfile _profile;
    readonly DictationBatcher _batcher;
    readonly object _gate = new();
    readonly List<(DictationBatch Batch, Task<BatchOutcome> Rewrite)> _batches = new();

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

    public void Push(string text, bool endedOnSilence)
    {
        lock (_gate)
        {
            DictationBatch? batch = _batcher.Push(text, endedOnSilence);
            if (batch is not null) Start(batch);
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
            if (batch is not null) Start(batch);
        }
    }

    // Blocks until every rewrite has returned, then joins the batches in
    // order: a paragraph break after a batch that ended on a pause, a space
    // otherwise. Call it from a background thread.
    public DictationBatchResult Assemble()
    {
        Complete();

        (DictationBatch Batch, Task<BatchOutcome> Rewrite)[] batches;
        lock (_gate) batches = _batches.ToArray();

        var text = new StringBuilder();
        int fallbacks = 0;
        long llmMs = 0, loadMs = 0, promptEvalMs = 0, evalMs = 0;
        int promptTokens = 0, evalTokens = 0;
        DictationBatch? previous = null;

        foreach (var (batch, rewrite) in batches)
        {
            BatchOutcome outcome;
            try
            {
                outcome = rewrite.GetAwaiter().GetResult();
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
            previous = batch;
        }

        return new DictationBatchResult(
            text.ToString(), batches.Length, fallbacks,
            llmMs, loadMs, promptEvalMs, evalMs, promptTokens, evalTokens);
    }

    // Called under _gate, so batches enter the list in Index order.
    void Start(DictationBatch batch)
    {
        DeckleLlmSource.Log.BatchRewriteSent();
        DeckleLlmSource.Log.BatchRewriteSentDetail(batch.Index, batch.Text.Length, _profile.Name);
        _batches.Add((batch, Task.Run(() => RewriteOne(batch))));
    }

    BatchOutcome RewriteOne(DictationBatch batch)
    {
        var sw = Stopwatch.StartNew();
        GenerationResult result = default;
        Exception? thrown = null;
        try
        {
            result = _service.Rewrite(batch.Text, _profile);
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
        return new BatchOutcome(rewritten, sw.ElapsedMilliseconds, result);
    }

    // Text is the trimmed rewrite, or null when the batch keeps its raw text.
    readonly record struct BatchOutcome(string? Text, long ElapsedMs, GenerationResult Result);
}
