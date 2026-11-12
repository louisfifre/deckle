using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── BatchRewrite partial — rewriting a live dictation block by block ──
    //
    // "Rewrite while I speak" (LlmSettings.Dictation), on the streaming
    // strategy: the plan read when a take starts, the feed that pushes each
    // transcribed utterance into the take's DictationBatchRewriter and hands
    // it to the highest step reached, with that step's profile and block
    // size, and the finalize branch that assembles the blocks instead of
    // rewriting the whole take.
    //
    // Why blocks: a whole-take rewrite starts at Stop, so the speaker waits
    // for the model to read and write the entire dictation. A block leaves
    // for the model as soon as it closes, while the speaker keeps talking, so
    // most of the rewriting hides behind the speech. At Stop only the last
    // block, the speech since the previous close, is still to rewrite; that
    // one request is what the speaker waits for (spec, Open 2).
    //
    // Why the monolithic strategy rewrites at Stop only: it transcribes the
    // take in one backend call at Stop, so it has no utterances to cut and no
    // recording time left to overlap with (RewriteProfileSelection.ForTake).
    //
    // Threads: the feed runs on the consumer task, in utterance order (the
    // order DictationBatchRewriter.Push requires). The finalize branch runs on
    // the worker thread, where FinalizeTranscription is synchronous, so the
    // blocking Assemble is fine there. Assemble takes no cancellation token,
    // like the whole-take Rewrite call: a Dispose during it waits for the
    // requests in flight, within the worker join timeout.

    // What the finalize needs from the live take: its plan, and its rewriter
    // when the take rewrites while the person speaks (null otherwise).
    internal sealed record StreamingRewrite(RewriteStepPlan Plan, DictationBatchRewriter? Rewriter);

    private StreamingRewriteTake StartStreamingRewrite()
    {
        LlmSettings llm = _host.Llm;
        RewriteStepPlan plan = RewriteProfileSelection.ForTake(llm, TakeProfileId, streamingTranscription: true);
        if (!plan.RewritesWhileSpeaking) return new StreamingRewriteTake(plan, null);

        return new StreamingRewriteTake(plan, new DictationBatchRewriter(_rewrite, plan.FirstBlockChars));
    }

    // The rewrite of one live take while the person speaks. Every block
    // closes in the document from the first utterance on; requests leave only
    // once a step is reached, so reaching one later needs no replay: the text
    // already closed is cut into the step's blocks and sent
    // (DictationBatchRewriter.Use). One step rewrites at a time, the highest
    // reached: the one before it sends nothing more.
    //
    // Threads: OnUtterance runs on the consumer task, in utterance order, and
    // is its only caller. Snapshot is read from the same task and from the
    // panel's handler.
    private sealed class StreamingRewriteTake(RewriteStepPlan plan, DictationBatchRewriter? rewriter)
    {
        int _rawChars;
        bool _pendingParagraphBreak;

        public RewriteStepPlan Plan => plan;
        public DictationBatchRewriter? Rewriter => rewriter;

        // The delivered text of the take so far, or null when nothing can
        // stream (the raw assembly is then the delivered text).
        public string? Snapshot() => rewriter?.Snapshot();

        public void OnUtterance(string text, bool endedOnSilence)
        {
            if (rewriter is null) return;

            // The count follows the raw assembly of the take (a space or a
            // paragraph break between utterances), the text the steps are
            // measured on at Stop.
            if (_rawChars > 0) _rawChars += _pendingParagraphBreak ? 2 : 1;
            _rawChars += text.Length;
            _pendingParagraphBreak = endedOnSilence;

            // Before the push: a block this utterance closes leaves with the
            // step the utterance reached, at that step's size.
            if (plan.Highest(_rawChars) is { Profile: { } profile } step)
                rewriter.Use(profile, step.BlockChars);
            rewriter.Push(text, endedOnSilence);
        }
    }

    // Subscribes the take to UtteranceTranscribed and returns the handle that
    // unsubscribes it, or null when nothing can stream. The caller disposes it
    // on every exit, so a later take or a file run never feeds a rewriter that
    // belongs to a finished take.
    //
    // The same handle forwards each reading that changes as a Revised snapshot
    // of the whole take, so a surface showing the text sees the rewrite land
    // where the raw text was, while the speaker is still talking.
    private IDisposable? FeedStreamingRewrite(StreamingRewriteTake take)
    {
        if (take.Rewriter is not { } rewriter) return null;

        Action onReturned = () => RaiseTranscriptChanged(rewriter.Snapshot(), TranscriptChangeKind.Revised);
        rewriter.BatchReturned += onReturned;

        Action<TranscribedUtterance> handler = u => take.OnUtterance(u.Text, u.EndedOnSilence);
        UtteranceTranscribed += handler;
        return new UtteranceSubscription(this, handler, () => rewriter.BatchReturned -= onReturned);
    }

    // The take rewrote while the person spoke: it is its blocks. What the
    // clipboard holds (the document's reading at Stop) is replaced only when
    // at least one block reads the last profile's rewrite and the text
    // changed: copying the same text again would add a duplicate to the
    // Windows clipboard history for nothing.
    //
    // LlmMs is the sum of every request's time (DictationBatchResult): it
    // measures the model's work over the take, not the wait the speaker saw
    // at Stop, since the requests overlap the recording and each other.
    private FinalizeRewrite ApplyPassageRewrite(
        string delivered, DictationBatchRewriter rewriter, RewriteProfile profile, int blockChars)
    {
        RaiseStatus(StatusPhase.Rewriting, Loc.Format("Status_Rewriting_Format", profile.Name));

        // The plan measures the final text, the feed measured utterance by
        // utterance: the same count, but a step crossed by the very last
        // utterance is taken here rather than trusted.
        rewriter.Use(profile, blockChars);
        DictationBatchResult result = rewriter.Assemble();

        string finalText = delivered;
        if (result.FallbackCount < result.BatchCount
            && !string.IsNullOrWhiteSpace(result.Text)
            && result.Text != delivered)
        {
            // A failed replacement leaves the earlier copy in place.
            if (CopyToClipboard(result.Text)) finalText = result.Text;
        }

        return new FinalizeRewrite(
            finalText,
            profile,
            result.LlmMs,
            result.LlmLoadMs,
            result.LlmPromptEvalMs,
            result.LlmEvalMs,
            result.LlmPromptTokens,
            result.LlmEvalTokens,
            result.LlmCacheWriteTokens,
            result.LlmCacheReadTokens);
    }

    // Also used by the recovery feed (TranscriptionEngine.Recovery.cs), which
    // has nothing else to unhook.
    private sealed class UtteranceSubscription(
        TranscriptionEngine engine,
        Action<TranscribedUtterance> handler,
        Action? onDispose = null) : IDisposable
    {
        public void Dispose()
        {
            engine.UtteranceTranscribed -= handler;
            onDispose?.Invoke();
        }
    }
}
