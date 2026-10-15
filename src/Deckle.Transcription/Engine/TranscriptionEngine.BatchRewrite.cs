using Deckle.Catalog;
using Deckle.Llm.Rewrite;

namespace Deckle.Transcription;

public sealed partial class TranscriptionEngine
{
    // ── BatchRewrite partial — per-batch rewriting of a live dictation ─────────
    //
    // Owns the opt-in LlmSettings.BatchRewriting for the streaming strategy:
    // the factory that reads the settings when a take starts, the feed that
    // pushes each transcribed utterance into the DictationBatchRewriter, and
    // the finalize branch that assembles the batches instead of rewriting the
    // whole take.
    //
    // Why batches: a whole-take rewrite starts at Stop, so the speaker waits for
    // the model to read and write the entire dictation. A batch leaves for the
    // model as soon as it closes, while the speaker keeps talking, so most of
    // the rewriting hides behind the speech. At Stop only the last batch, the
    // speech since the previous close, is still to rewrite; that one request is
    // what the speaker waits for, which is why the threshold bounds it.
    //
    // Why the monolithic strategy keeps the whole-take rewrite: it transcribes
    // the take in one backend call at Stop, so it has no utterances to batch
    // and no recording time left to overlap with.
    //
    // Threads: the feed runs on the consumer task, in utterance order (the
    // order DictationBatchRewriter.Push requires). The finalize branch runs on
    // the worker thread, where FinalizeTranscription is synchronous, so the
    // blocking Assemble is fine there. Assemble takes no cancellation token,
    // like the whole-take Rewrite call it replaces: a Dispose during it waits
    // for the requests in flight, within the worker join timeout.

    // The rewriter of one take and the profile its batches were sent with. The
    // profile is captured at the start of the take, so a Settings change during
    // the recording cannot label the take with a profile its batches never used.
    internal sealed record BatchRewriteRun(DictationBatchRewriter Rewriter, RewriteProfile Profile);

    // Null when batching is off or no active profile resolves: the take then
    // follows the whole-take rewrite (or none) at finalize, as before.
    private BatchRewriteRun? StartBatchRewrite()
    {
        LlmSettings llm = _host.Llm;
        if (!llm.BatchRewriting.Enabled) return null;

        RewriteProfile? profile = RewriteProfileSelection.ForDictation(llm);
        if (profile is null) return null;

        // The core does not clamp: a hand-edited settings file could hold any
        // value, and DictationBatcher rejects zero or less.
        int threshold = Math.Clamp(
            llm.BatchRewriting.ThresholdChars,
            BatchRewriteSettings.MinThresholdChars,
            BatchRewriteSettings.MaxThresholdChars);

        return new BatchRewriteRun(
            new DictationBatchRewriter(_rewrite, profile, threshold),
            profile);
    }

    // Subscribes the rewriter to UtteranceTranscribed and returns the handle
    // that unsubscribes it, or null when the take has no batch run. The caller
    // disposes it on every exit, so a later take or a file run never feeds a
    // rewriter that belongs to a finished take.
    private IDisposable? FeedBatchRewrite(BatchRewriteRun? run)
    {
        if (run is null) return null;

        DictationBatchRewriter rewriter = run.Rewriter;
        Action<TranscribedUtterance> handler = u => rewriter.Push(u.Text, u.EndedOnSilence);
        UtteranceTranscribed += handler;
        return new UtteranceSubscription(this, handler);
    }

    // Replaces the whole-take rewrite when the take was batched. The raw text is
    // already on the clipboard; the assembled rewrite replaces it only when at
    // least one batch came back rewritten. When every batch fell back, the
    // assembly is the raw text again, and copying it would add a duplicate to
    // the Windows clipboard history for nothing.
    //
    // LlmMs is the sum of every batch's request time (DictationBatchResult): it
    // measures the model's work over the take, not the wait the speaker saw at
    // Stop, since the batches overlap the recording and each other.
    private FinalizeRewrite ApplyBatchRewrite(string rawText, BatchRewriteRun run)
    {
        RaiseStatus(Loc.Format("Status_Rewriting_Format", run.Profile.Name));
        DictationBatchResult result = run.Rewriter.Assemble();

        string finalText = rawText;
        if (result.FallbackCount < result.BatchCount && !string.IsNullOrWhiteSpace(result.Text))
        {
            finalText = result.Text;
            // A failed replacement leaves the raw copy in place.
            CopyToClipboard(finalText);
        }

        return new FinalizeRewrite(
            finalText,
            run.Profile,
            result.LlmMs,
            result.LlmLoadMs,
            result.LlmPromptEvalMs,
            result.LlmEvalMs,
            result.LlmPromptTokens,
            result.LlmEvalTokens);
    }

    private sealed class UtteranceSubscription(
        TranscriptionEngine engine,
        Action<TranscribedUtterance> handler) : IDisposable
    {
        public void Dispose() => engine.UtteranceTranscribed -= handler;
    }
}
