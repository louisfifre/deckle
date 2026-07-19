using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── Rewrite ─────────────────────────────────────────────────────────

    [Event(EvtRewriteSkippedNoModel,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A rewrite was skipped because the profile has no model — set one in Settings → LLM")]
    public void RewriteSkippedNoModel()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoModel);
    }

    [Event(EvtRewriteSkippedNoModelDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite skipped no model | profile={0}")]
    public void RewriteSkippedNoModelDetail(string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoModelDetail, profile);
    }

    [Event(EvtRewriteStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriting")]
    public void RewriteStarted()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStarted);
    }

    [Event(EvtRewriteStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "request | chars={0} | model={1} | profile={2} | family={3} | {4}")]
    public void RewriteStartedDetail(int chars, string model, string profile, string family, string options)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStartedDetail, chars, model, profile, family, options);
    }

    [Event(EvtRewriteCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite complete")]
    public void RewriteCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCompleted);
    }

    [Event(EvtRewriteCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite complete | ms={0} | in_chars={1} | out_chars={2} | profile={3}")]
    public void RewriteCompletedDetail(long ms, int in_chars, int out_chars, string profile)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCompletedDetail, ms, in_chars, out_chars, profile);
    }

    [Event(EvtRewriteMetrics,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "{0}")]
    public void RewriteMetrics(string metrics_text)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteMetrics, metrics_text);
    }

    [Event(EvtRewriteTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite timed out")]
    public void RewriteTimeout()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeout);
    }

    [Event(EvtRewriteTimeoutDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite timeout | cap_min={0:F0} | profile={1} | model={2}")]
    public void RewriteTimeoutDetail(double cap_min, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeoutDetail, cap_min, profile, model);
    }

    [Event(EvtRewriteUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriter unavailable")]
    public void RewriteUnavailable()
    {
        if (IsEnabled()) WriteEvent(EvtRewriteUnavailable);
    }

    [Event(EvtRewriteUnavailableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite unavailable | error={0} | message={1} | profile={2} | model={3}")]
    public void RewriteUnavailableDetail(string ex_type, string message, string profile, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteUnavailableDetail, ex_type, message, profile, model);
    }

}
