using System.Diagnostics;
using System.Linq;
using Deckle.Llm;

namespace Deckle.Llm.Rewrite;

// The streaming rewrite of one dictation: feeds the utterances to a
// DictationBatcher, keeps the passages it closes in the take's
// DictationDocument, and, once a streaming step is reached (Use), sends each
// closed passage to the rewrite service at once, on the thread pool, so the
// model works while the speaker keeps talking and only the last passage is
// left to wait for at Stop.
//
// Before Use, passages close and nothing leaves: the document reads raw.
// Each Use opens a generation with a new profile, and the passages already
// closed are sent again with it, from their raw text: a dictation is always
// rewritten from its beginning, by one voice. A passage keeps the reading it
// had until its new one comes back (DictationDocument's guarantees).
//
// Threads: the transcription consumer calls Push and Use, the engine's worker
// calls Complete and Assemble, the document may be read from any thread. One
// lock guards the batcher and the requests; Assemble releases it before
// waiting, so a slow rewrite never blocks a Push.
//
// A passage whose rewrite fails or comes back blank keeps what it showed.
// Losing a stretch of the dictation would be worse than delivering it
// unrewritten, and the rest of the take still gets its rewrites;
// FallbackCount tells the take how many passages do not read the last
// profile's rewrite.
//
// The provider's prompt cache: every passage sends the profile's whole prompt
// for a few sentences of text, so the passages ask the provider to keep it
// (IRewriteService.RewriteBatch). A cached prompt can only be read by a
// request that leaves after the one that wrote it has been taken in: requests
// sent together all pay the write, a quarter above the plain price, and none
// reads (measured on 2026-11-11 with the benchmark's cache-probe: 13 to 17 %
// dearer than no cache at all, against a third cheaper one after the other).
// So the first request of a generation leaves alone, and the others that
// leave while it is still out wait for its answer. While the person speaks
// that wait is hidden: passages close forty seconds apart, and only the burst
// that rewrites the beginning again at a new generation ever queues. The
// passage flushed at Stop is the one the person waits for, so it never
// queues, whatever that costs.
public sealed class DictationBatchRewriter
{
    readonly IRewriteService _service;
    readonly DictationBatcher _batcher;
    readonly DictationDocument _document = new();
    readonly object _gate = new();

    // Every request sent, for the take's figures, and the last one sent for
    // each passage, the one Assemble waits for. Guarded by _gate.
    readonly List<Request> _requests = new();
    readonly List<Request?> _latest = new();

    // The streaming profile and its generation; null and 0 before Use.
    // Guarded by _gate.
    RewriteProfile? _profile;
    int _generation;

    // The first request of the generation sent with the prompt cached, the
    // one that writes it. Guarded by _gate.
    Task? _cacheWriter;

    public DictationBatchRewriter(IRewriteService service, int thresholdChars)
    {
        _service = service;
        _batcher = new DictationBatcher(thresholdChars);
    }

    public DictationDocument Document => _document;

    // The profile passages are rewritten with, or null before any streaming
    // step was reached.
    public RewriteProfile? Profile
    {
        get { lock (_gate) return _profile; }
    }

    // Passages closed so far, the one Complete flushed included.
    public int BatchCount => _document.PassageCount;

    // Raised on the thread-pool thread of a request once its rewrite changed
    // what the document reads, after Snapshot already reflects it.
    public event Action? BatchReturned;

    public void Push(string text, bool endedOnSilence)
    {
        lock (_gate)
        {
            DictationBatch? batch = _batcher.Push(text, endedOnSilence);
            if (batch is not null) Close(batch, flushed: false);
            _document.SetOpen(_batcher.OpenText);
        }
    }

    // Rewrites with profile from now on: a new generation, and every passage
    // already closed leaves again with it, from its raw text. The same
    // profile again changes nothing.
    public void Use(RewriteProfile profile)
    {
        lock (_gate)
        {
            if (_profile is not null && _profile.Id == profile.Id) return;

            _profile = profile;
            _generation++;
            _cacheWriter = null;

            DictationBatch[] closed = _document.Batches();
            DeckleLlmSource.Log.BatchRewriteProfileChanged();
            DeckleLlmSource.Log.BatchRewriteProfileChangedDetail(_generation, profile.Name, closed.Length);
            foreach (DictationBatch batch in closed) Send(batch, flushed: false);
        }
    }

    // Flushes the open passage and, once a profile streams, starts its
    // rewrite. Calling it again with nothing pushed in between finds nothing
    // to flush, which makes it idempotent. A Push after Complete is not lost:
    // it opens a new passage that the next Complete (Assemble calls one)
    // flushes.
    public void Complete()
    {
        lock (_gate)
        {
            DictationBatch? batch = _batcher.Flush();
            if (batch is not null) Close(batch, flushed: true);
            _document.SetOpen("");
        }
    }

    // The take as it reads right now. Never blocks.
    public string Snapshot() => _document.Snapshot();

    // Blocks until the last request of every passage has returned, then
    // returns the document's text. Call it from a background thread.
    //
    // The figures sum every request that has returned by then, the passages
    // sent again at a new generation included: they were paid for. A request
    // a newer one overtook and that is still out is not waited for, and so
    // not counted.
    public DictationBatchResult Assemble()
    {
        Complete();

        Request?[] latest;
        int generation;
        lock (_gate)
        {
            latest = _latest.ToArray();
            generation = _generation;
        }

        foreach (Request? request in latest)
        {
            if (request is null) continue;
            try
            {
                request.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // RewriteOne catches everything itself; this guards the task
                // machinery, not the service. RewriteOne never logged this
                // request's end, so the fallback is logged here.
                DeckleLlmSource.Log.BatchRewriteFellBack(
                    request.Batch.Index, "task_faulted", ex.GetType().Name, ex.Message);
            }
        }

        Request[] all;
        lock (_gate) all = _requests.ToArray();

        long llmMs = 0, loadMs = 0, promptEvalMs = 0, evalMs = 0;
        int promptTokens = 0, evalTokens = 0, cacheWriteTokens = 0, cacheReadTokens = 0;
        foreach (Request request in all)
        {
            if (request.Outcome is not { } outcome) continue;
            llmMs        += outcome.ElapsedMs;
            loadMs       += outcome.Result.LoadMs;
            promptEvalMs += outcome.Result.PromptEvalMs;
            evalMs       += outcome.Result.EvalMs;
            promptTokens += outcome.Result.PromptTokens;
            evalTokens   += outcome.Result.EvalTokens;
            cacheWriteTokens += outcome.Result.CacheWriteTokens;
            cacheReadTokens  += outcome.Result.CacheReadTokens;
        }

        // A passage that does not read the last profile's rewrite: never
        // rewritten, or still on an earlier profile's reading.
        int[] shown = _document.Generations();
        int fallbacks = shown.Count(g => g == 0 || g < generation);

        return new DictationBatchResult(
            _document.Snapshot(), shown.Length, fallbacks,
            llmMs, loadMs, promptEvalMs, evalMs, promptTokens, evalTokens,
            cacheWriteTokens, cacheReadTokens);
    }

    // Called under _gate, so passages enter the document in Index order. The
    // passage is in the document before its request starts, so a Snapshot
    // taken from BatchReturned always sees the passage the event is about.
    void Close(DictationBatch batch, bool flushed)
    {
        _document.Add(batch);
        _latest.Add(null);
        if (_profile is not null) Send(batch, flushed);
    }

    // Called under _gate, with a profile set.
    //
    // flushed: the passage was closed by Complete, at Stop, and not by the
    // speech going on. See the class comment for what it changes.
    void Send(DictationBatch batch, bool flushed)
    {
        // A take that ends as one passage has no second request to read the
        // prompt back: caching it would only pay the write.
        bool cached = !(flushed && _document.PassageCount == 1);
        var request = new Request(batch, _generation, _profile!);
        _requests.Add(request);
        _latest[batch.Index] = request;

        if (!flushed && _cacheWriter is { } writer)
        {
            // Runs whatever the writer's outcome: a failed first request must
            // not hold the others back, and the next one then writes the cache.
            request.Task = writer.ContinueWith(
                _ => RewriteOne(request, cached),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }

        request.Task = Task.Run(() => RewriteOne(request, cached));
        if (cached) _cacheWriter ??= request.Task;
    }

    BatchOutcome RewriteOne(Request request, bool cached)
    {
        DictationBatch batch = request.Batch;
        // Logged here, where the request really leaves: one that waited for
        // the cache writer's answer would otherwise read as sent while it
        // queued.
        DeckleLlmSource.Log.BatchRewriteSent();
        DeckleLlmSource.Log.BatchRewriteSentDetail(batch.Index, batch.Text.Length, request.Profile.Name);
        var sw = Stopwatch.StartNew();
        GenerationResult result = default;
        Exception? thrown = null;
        try
        {
            result = cached
                ? _service.RewriteBatch(batch.Text, request.Profile)
                : _service.Rewrite(batch.Text, request.Profile);
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

        var outcome = new BatchOutcome(rewritten, sw.ElapsedMilliseconds, result);
        request.Outcome = outcome;

        // Applied before the event, so a Snapshot taken from the handler reads
        // the rewrite; the task itself completes only after this returns. A
        // reading an answer of a later generation already replaced is dropped.
        if (_document.Apply(batch.Index, request.Generation, rewritten))
            BatchReturned?.Invoke();
        else if (rewritten is not null)
            DeckleLlmSource.Log.BatchRewriteReadingIgnored(batch.Index, request.Generation);

        return outcome;
    }

    // One rewrite of one passage, with the generation and profile it left
    // with. Task is set under _gate right after the request is listed;
    // Outcome once it has returned.
    sealed class Request(DictationBatch batch, int generation, RewriteProfile profile)
    {
        public DictationBatch Batch { get; } = batch;
        public int Generation { get; } = generation;
        public RewriteProfile Profile { get; } = profile;
        public Task<BatchOutcome> Task { get; set; } = System.Threading.Tasks.Task.FromResult(new BatchOutcome(null, 0, default));
        // Boxed so the write is one reference, published whole: Assemble may
        // read a request a newer one overtook while it is still out.
        public BatchOutcome? Outcome
        {
            get => (BatchOutcome?)_outcome;
            set => _outcome = value;
        }
        volatile object? _outcome;
    }

    // Text is the trimmed rewrite, or null when the passage keeps its reading.
    readonly record struct BatchOutcome(string? Text, long ElapsedMs, GenerationResult Result);
}
