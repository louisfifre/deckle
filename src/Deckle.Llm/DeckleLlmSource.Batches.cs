using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── DictationBatchRewriter ──────────────────────────────────────────
    //
    // One SentDetail per batch when its rewrite starts, then one ReturnedDetail
    // or one FellBack when it ends. The run-story lines are RewriteService's
    // TranscriptRewriteStarted and TranscriptRewriteCompleted, written at the
    // same instant: an Informational line here said the same thing twice
    // (BatchRewriteSent, 48, and BatchRewriteReturned, 50, are retired). The
    // Detail lines with mixed parameter types pass an explicit object[]: their
    // signatures match no typed WriteEvent overload, and an explicit array
    // keeps a near-match overload from ever being picked by implicit
    // conversion.

    [Event(EvtBatchRewriteSentDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch={0} | chars={1} | profile={2}")]
    public void BatchRewriteSentDetail(int batch_index, int chars, string profile)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteSentDetail, new object[] { batch_index, chars, profile });
    }

    [Event(EvtBatchRewriteReturnedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch={0} | elapsed_ms={1} | in_chars={2} | out_chars={3}")]
    public void BatchRewriteReturnedDetail(int batch_index, long elapsed_ms, int in_chars, int out_chars)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteReturnedDetail, new object[] { batch_index, elapsed_ms, in_chars, out_chars });
    }

    // The dictation reached a higher step (or its first one) while the
    // person speaks: the text already closed is cut into that step's blocks
    // and leaves again with its profile, from its raw text. generation counts
    // the steps that took over; passages is how many blocks leave again, 0
    // when the step was reached before any block closed; block_chars is the
    // step's block size.
    [Event(EvtBatchRewriteProfileChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The dictation reached a step: what was said is rewritten in that step's blocks")]
    public void BatchRewriteProfileChanged()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteProfileChanged);
    }

    [Event(EvtBatchRewriteProfileChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "generation={0} | profile={1} | passages={2} | block_chars={3}")]
    public void BatchRewriteProfileChangedDetail(int generation, string profile, int passages, int block_chars)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteProfileChangedDetail, new object[] { generation, profile, passages, block_chars });
    }

    // A rewrite came back after a newer one of the same passage: the passage
    // keeps the newer reading and this one is dropped.
    [Event(EvtBatchRewriteReadingIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch={0} | generation={1}")]
    public void BatchRewriteReadingIgnored(int batch_index, int generation)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteReadingIgnored, batch_index, generation);
    }

    // reason: no_result (the service gave no text; its own Warning just
    // before says why), blank (it answered whitespace), exception (it threw;
    // ex_type and ex_message say what), task_faulted (the batch task itself
    // faulted). ex_type and ex_message are empty unless something threw.
    [Event(EvtBatchRewriteFellBack,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A dictation batch could not be rewritten and keeps its raw text\nreason={1} | batch={0} | ex_type={2} | ex_message={3}")]
    public void BatchRewriteFellBack(int batch_index, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteFellBack, new object[] { batch_index, reason, ex_type, ex_message });
    }
}
