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

    // The dictation reached a higher streaming step (or its first one): the
    // passages already closed leave again with that step's profile, from
    // their raw text. generation counts the profiles the take streamed with;
    // passages is how many leave again, 0 when the step was reached before
    // any passage closed.
    [Event(EvtBatchRewriteProfileChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The dictation reached a streaming step: its passages are rewritten with that step's profile")]
    public void BatchRewriteProfileChanged()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteProfileChanged);
    }

    [Event(EvtBatchRewriteProfileChangedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "generation={0} | profile={1} | passages={2}")]
    public void BatchRewriteProfileChangedDetail(int generation, string profile, int passages)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteProfileChangedDetail, new object[] { generation, profile, passages });
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

    // At Stop the highest step reached rewrites the whole text, so the
    // passages streamed during the dictation are not assembled: their reading
    // went to the clipboard first and is replaced when the whole rewrite
    // returns. The requests already sent were paid for.
    [Event(EvtBatchRewriteSuperseded,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The highest step reached rewrites the whole text at Stop, after the streamed passages")]
    public void BatchRewriteSuperseded()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteSuperseded);
    }

    [Event(EvtBatchRewriteSupersededDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "passages={0} | streamed_profile={1} | stop_profile={2}")]
    public void BatchRewriteSupersededDetail(int passages, string streamed_profile, string stop_profile)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteSupersededDetail, new object[] { passages, streamed_profile, stop_profile });
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
