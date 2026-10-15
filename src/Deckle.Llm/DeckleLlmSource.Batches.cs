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
           Message = "batch rewrite sent | batch={0} | chars={1} | profile={2}")]
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
           Message = "batch rewrite returned | batch={0} | ms={1} | in_chars={2} | out_chars={3}")]
    public void BatchRewriteReturnedDetail(int batch_index, long elapsed_ms, int in_chars, int out_chars)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteReturnedDetail, new object[] { batch_index, elapsed_ms, in_chars, out_chars });
    }

    [Event(EvtBatchRewriteFellBack,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A dictation batch kept its raw text")]
    public void BatchRewriteFellBack()
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteFellBack);
    }

    [Event(EvtBatchRewriteFellBackDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "batch rewrite fell back | batch={0}")]
    public void BatchRewriteFellBackDetail(int batch_index)
    {
        if (IsEnabled()) WriteEvent(EvtBatchRewriteFellBackDetail, batch_index);
    }
}
