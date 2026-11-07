using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── DictationBatchRewriter ──────────────────────────────────────────
    //
    // One Sent per batch when its rewrite starts, then one Returned or one
    // FellBack when it ends. The Detail mirrors with mixed parameter types pass
    // an explicit object[]: their signatures match no typed WriteEvent
    // overload, and an explicit array keeps a near-match overload from ever
    // being picked by implicit conversion.

    [Event(EvtBatchRewriteSent,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A dictation batch left for rewriting")]
    public void BatchRewriteSent()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteSent);
    }

    [Event(EvtBatchRewriteSentDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch={0} | chars={1} | profile={2}")]
    public void BatchRewriteSentDetail(int batch_index, int chars, string profile)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteSentDetail, new object[] { batch_index, chars, profile });
    }

    [Event(EvtBatchRewriteReturned,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A dictation batch came back rewritten")]
    public void BatchRewriteReturned()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteReturned);
    }

    [Event(EvtBatchRewriteReturnedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch={0} | elapsed_ms={1} | in_chars={2} | out_chars={3}")]
    public void BatchRewriteReturnedDetail(int batch_index, long elapsed_ms, int in_chars, int out_chars)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteReturnedDetail, new object[] { batch_index, elapsed_ms, in_chars, out_chars });
    }

    // A run opened at a length step of the ladder was dropped at Stop because
    // the final length points to another profile (or to none): the whole take
    // is rewritten at once instead. The batches already sent are wasted work.
    [Event(EvtBatchRewriteDiscarded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The batches sent during the dictation were dropped: the final length calls for another profile")]
    public void BatchRewriteDiscarded()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteDiscarded);
    }

    // final_profile is empty when the final length resolves to no profile.
    [Event(EvtBatchRewriteDiscardedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batches={0} | started_profile={1} | final_profile={2}")]
    public void BatchRewriteDiscardedDetail(int batches, string started_profile, string final_profile)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteDiscardedDetail, new object[] { batches, started_profile, final_profile });
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
