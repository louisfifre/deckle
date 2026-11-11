using Deckle.Llm;
using Deckle.Llm.Rewrite;
using Deckle.Localization;

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
    // profile is captured when the run opens, so a Settings change during the
    // recording cannot label the take with a profile its batches never used.
    //
    // StartedAtLengthStep: the run did not open with the take but when the raw
    // text first reached a step of the length ladder (BatchRewriteTake). The
    // ladder can still send the finished take to a higher step, so such a run
    // is checked against the final length before it is kept.
    internal sealed record BatchRewriteRun(
        DictationBatchRewriter Rewriter, RewriteProfile Profile, bool StartedAtLengthStep = false);

    // Null when batching is off, or when no profile can ever resolve for this
    // take: no profile at the start and no length ladder to give one later.
    // The take then follows the whole-take rewrite (or none) at finalize.
    //
    // With no profile at the start but a length ladder on, the take is kept
    // "deferred": it opens its run at the first step the text reaches, since
    // before that the whole-take rewrite would not touch it either.
    private BatchRewriteTake? StartBatchRewrite()
    {
        LlmSettings llm = _host.Llm;
        if (!llm.BatchRewriting.Enabled) return null;

        RewriteProfile? profile = RewriteProfileSelection.ForDictation(
            llm, rawChars: null, TakeProfileId);
        bool waitsForLengthStep = profile is null
            && llm.Enabled
            && llm.LengthSwitch is { Enabled: true };
        if (profile is null && !waitsForLengthStep) return null;

        // The core does not clamp: a hand-edited settings file could hold any
        // value, and DictationBatcher rejects zero or less.
        int threshold = Math.Clamp(
            llm.BatchRewriting.ThresholdChars,
            BatchRewriteSettings.MinThresholdChars,
            BatchRewriteSettings.MaxThresholdChars);

        return new BatchRewriteTake(this, profile, TakeProfileId, threshold);
    }

    // The batch rewriting of one live take. It owns the one thing that can
    // change during the take: whether the run exists yet.
    //
    // Threads: OnUtterance runs on the consumer task, in utterance order, and
    // is the only writer of the run. Snapshot and Run are read from the same
    // task (the delivered text) and from the worker after the consumer drain
    // (the finalize), so the run is published through a volatile field.
    private sealed class BatchRewriteTake
    {
        readonly TranscriptionEngine _engine;
        readonly RewriteProfile? _initialProfile;
        readonly string? _forcedProfileId;
        readonly int _threshold;

        // The utterances of a deferred take, until its run opens and replays
        // them. Text and silence flag are all a DictationBatcher reads.
        readonly List<(string Text, bool EndedOnSilence)> _kept = new();
        int _rawChars;
        bool _pendingParagraphBreak;
        Action<DictationBatchRewriter>? _attach;
        volatile BatchRewriteRun? _run;

        public BatchRewriteTake(
            TranscriptionEngine engine, RewriteProfile? initialProfile,
            string? forcedProfileId, int threshold)
        {
            _engine = engine;
            _initialProfile = initialProfile;
            _forcedProfileId = forcedProfileId;
            _threshold = threshold;
        }

        public BatchRewriteRun? Run => _run;

        // The delivered text of the take so far, or null before a deferred
        // run opens (the raw assembly is then the delivered text).
        public string? Snapshot() => _run?.Rewriter.Snapshot();

        // attach runs on each new rewriter before it receives any utterance,
        // so no returned batch can slip past its subscriber.
        public void Begin(Action<DictationBatchRewriter> attach)
        {
            _attach = attach;
            if (_initialProfile is not null) Open(_initialProfile, startedAtLengthStep: false);
        }

        public void OnUtterance(string text, bool endedOnSilence)
        {
            if (_run is { } run)
            {
                run.Rewriter.Push(text, endedOnSilence);
                return;
            }

            _kept.Add((text, endedOnSilence));

            // The count follows the raw assembly of the take (a space or a
            // paragraph break between utterances), because the final choice
            // is made on that text's length.
            if (_rawChars > 0) _rawChars += _pendingParagraphBreak ? 2 : 1;
            _rawChars += text.Length;
            _pendingParagraphBreak = endedOnSilence;

            RewriteProfile? profile = RewriteProfileSelection.ForDictation(
                _engine._host.Llm, _rawChars, _forcedProfileId);
            if (profile is null) return;

            Open(profile, startedAtLengthStep: true);
        }

        // The run to finalize with: the one the take opened, or null when the
        // whole take is to be rewritten at once. A run opened at a length step
        // is kept only if the final length points to the same profile; the
        // batches already sent finish on their own and their results are
        // dropped with the rewriter.
        public BatchRewriteRun? RunForFinalize(int finalRawChars)
        {
            if (_run is not { } run) return null;
            if (!run.StartedAtLengthStep) return run;

            RewriteProfile? final = RewriteProfileSelection.ForDictation(
                _engine._host.Llm, finalRawChars, _forcedProfileId);
            if (final is not null && final.Id == run.Profile.Id) return run;

            DeckleLlmSource.Log.BatchRewriteDiscarded();
            DeckleLlmSource.Log.BatchRewriteDiscardedDetail(
                run.Rewriter.BatchCount, run.Profile.Name, final?.Name ?? "");
            return null;
        }

        void Open(RewriteProfile profile, bool startedAtLengthStep)
        {
            var rewriter = new DictationBatchRewriter(_engine._rewrite, profile, _threshold);
            _attach?.Invoke(rewriter);

            // Replay in order, text and silence flag: the batcher is pure, so
            // this closes the same batches as if the run had been there from
            // the first utterance. The catch-up burst leaves for the rewrite
            // service at once.
            foreach ((string text, bool endedOnSilence) in _kept)
                rewriter.Push(text, endedOnSilence);
            _kept.Clear();

            _run = new BatchRewriteRun(rewriter, profile, startedAtLengthStep);
        }
    }

    // Subscribes the take to UtteranceTranscribed and returns the handle that
    // unsubscribes it, or null when the take has no batch rewriting. The caller
    // disposes it on every exit, so a later take or a file run never feeds a
    // rewriter that belongs to a finished take.
    //
    // The same handle forwards each returned batch as a Revised snapshot of
    // the whole take, so a surface showing the text sees the rewrite land
    // where the raw text was, while the speaker is still talking.
    private IDisposable? FeedBatchRewrite(BatchRewriteTake? take)
    {
        if (take is null) return null;

        Action? detach = null;
        take.Begin(rewriter =>
        {
            Action onReturned = () => RaiseTranscriptChanged(rewriter.Snapshot(), TranscriptChangeKind.Revised);
            rewriter.BatchReturned += onReturned;
            detach = () => rewriter.BatchReturned -= onReturned;
        });

        Action<TranscribedUtterance> handler = u => take.OnUtterance(u.Text, u.EndedOnSilence);
        UtteranceTranscribed += handler;
        return new UtteranceSubscription(this, handler, () => detach?.Invoke());
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
        RaiseStatus(StatusPhase.Rewriting, Loc.Format("Status_Rewriting_Format", run.Profile.Name));
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
