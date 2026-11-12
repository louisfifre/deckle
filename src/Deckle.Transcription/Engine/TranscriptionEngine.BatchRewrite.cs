using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── BatchRewrite partial — rewriting a live dictation passage by passage ──
    //
    // The streaming steps of LlmSettings.Dictation, on the streaming strategy:
    // the plan read when a take starts, the feed that pushes each transcribed
    // utterance into the take's DictationBatchRewriter and moves it to the
    // profile of the highest streaming step reached, and the finalize branch
    // that assembles the passages instead of rewriting the whole take.
    //
    // Why passages: a whole-take rewrite starts at Stop, so the speaker waits
    // for the model to read and write the entire dictation. A passage leaves
    // for the model as soon as it closes, while the speaker keeps talking, so
    // most of the rewriting hides behind the speech. At Stop only the last
    // passage, the speech since the previous close, is still to rewrite; that
    // one request is what the speaker waits for, which is why its size is
    // bounded.
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
    // when a step could stream (null otherwise).
    internal sealed record StreamingRewrite(RewriteStepPlan Plan, DictationBatchRewriter? Rewriter);

    private StreamingRewriteTake StartStreamingRewrite()
    {
        LlmSettings llm = _host.Llm;
        RewriteStepPlan plan = RewriteProfileSelection.ForTake(llm, TakeProfileId, streamingTranscription: true);
        if (!plan.HasStreamingStep) return new StreamingRewriteTake(plan, null);

        // The core does not clamp: a hand-edited settings file could hold any
        // value, and DictationBatcher rejects zero or less.
        int passageChars = Math.Clamp(
            llm.Dictation.Steps[0].BlockChars,
            DictationRewriteSettings.MinBlockChars,
            DictationRewriteSettings.MaxBlockChars);

        return new StreamingRewriteTake(plan, new DictationBatchRewriter(_rewrite, passageChars));
    }

    // The streaming rewrite of one live take. Every passage closes in the
    // document from the first utterance on; requests leave only once a
    // streaming step is reached, so reaching one later needs no replay: the
    // passages already closed are simply sent (DictationBatchRewriter.Use).
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

            // Before the push: a passage this utterance closes leaves with
            // the step the utterance reached.
            if (plan.HighestStreaming(_rawChars) is { Profile: { } profile })
                rewriter.Use(profile);
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

    // The highest step reached streams: the take is its passages. What the
    // clipboard holds (the document's reading at Stop) is replaced only when
    // at least one passage reads the last profile's rewrite and the text
    // changed: copying the same text again would add a duplicate to the
    // Windows clipboard history for nothing.
    //
    // LlmMs is the sum of every request's time (DictationBatchResult): it
    // measures the model's work over the take, not the wait the speaker saw
    // at Stop, since the requests overlap the recording and each other.
    private FinalizeRewrite ApplyPassageRewrite(
        string delivered, DictationBatchRewriter rewriter, RewriteProfile profile)
    {
        RaiseStatus(StatusPhase.Rewriting, Loc.Format("Status_Rewriting_Format", profile.Name));

        // The plan measures the final text, the feed measured utterance by
        // utterance: the same count, but a step crossed by the very last
        // utterance is taken here rather than trusted.
        rewriter.Use(profile);
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
