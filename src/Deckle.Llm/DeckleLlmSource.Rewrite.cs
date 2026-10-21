using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── Rewrite ─────────────────────────────────────────────────────────
    //
    // caller is the kind of rewrite (transcript, paragraph, sentence),
    // provider the provider id the profile names, engine the protocol behind
    // it: a provider added in Settings carries a generated id that says
    // nothing on its own. Signatures with mixed parameter types pass an
    // explicit object[]: they match no typed WriteEvent overload, and an
    // explicit array keeps a near-match overload from ever being picked by
    // implicit conversion.

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

    // reason: no_provider (the profile names none), provider_missing (its id
    // is no longer in the list), provider_disabled, unresolved (anything
    // else the resolver refused).
    [Event(EvtRewriteSkippedNoProvider,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A rewrite was skipped because its provider is missing or disabled — check Settings → LLM | reason={1} | caller={0}")]
    public void RewriteSkippedNoProvider(string caller, string reason)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoProvider, caller, reason);
    }

    [Event(EvtRewriteSkippedNoProviderDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite skipped no provider | profile={0} | provider={1}")]
    public void RewriteSkippedNoProviderDetail(string profile, string provider)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteSkippedNoProviderDetail, profile, provider);
    }

    [Event(EvtRewriteStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriting | caller={0} | provider={1} | engine={2} | model={3}")]
    public void RewriteStarted(string caller, string provider, string engine, string model)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStarted, new object[] { caller, provider, engine, model });
    }

    [Event(EvtRewriteStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "request | chars={0} | model={1} | profile={2} | family={3} | {4}")]
    public void RewriteStartedDetail(int chars, string model, string profile, string family, string options)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteStartedDetail, chars, model, profile, family, options);
    }

    // What the engine received back, emitted by the engine before the
    // service closes the rewrite. stop_reason is the provider's own word
    // (stop, length, end_turn, max_tokens), empty when it reports none: a
    // truncated answer shows here first.
    [Event(EvtRewriteResponseDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite response | elapsed_ms={0} | in_chars={1} | out_chars={2} | profile={3} | load_ms={4} | prompt_eval_ms={5} | eval_ms={6} | prompt_tokens={7} | eval_tokens={8} | stop_reason={9}")]
    public void RewriteResponseDetail(
        long elapsed_ms, int in_chars, int out_chars, string profile,
        long load_ms, long prompt_eval_ms, long eval_ms,
        int prompt_tokens, int eval_tokens, string stop_reason)
    {
        if (IsEnabled())
            WriteEvent(EvtRewriteResponseDetail, new object[]
            {
                elapsed_ms, in_chars, out_chars, profile,
                load_ms, prompt_eval_ms, eval_ms,
                prompt_tokens, eval_tokens, stop_reason,
            });
    }

    [Event(EvtRewriteCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite complete | caller={0} | provider={1} | elapsed_ms={2}")]
    public void RewriteCompleted(string caller, string provider, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCompleted, new object[] { caller, provider, elapsed_ms });
    }

    // An interactive rewrite whose caller withdrew it: the offer deadline
    // passed, or a keystroke made the paragraph stale. Expected, so not a
    // Warning, but it still closes the rewrite it started.
    [Event(EvtRewriteCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite cancelled by its caller | caller={0} | provider={1} | elapsed_ms={2}")]
    public void RewriteCancelled(string caller, string provider, long elapsed_ms)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteCancelled, new object[] { caller, provider, elapsed_ms });
    }

    [Event(EvtRewriteTimeout,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewrite timed out | caller={0} | provider={1} | model={2} | elapsed_ms={3} | cap_s={4:F0}")]
    public void RewriteTimeout(string caller, string provider, string model, long elapsed_ms, double cap_s)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteTimeout, new object[] { caller, provider, model, elapsed_ms, cap_s });
    }

    // reason is the failure class RewriteFailureMessage.ReasonOf derives,
    // the same reading the overlay card words for the user. status is the
    // HTTP status when the server answered, 0 when it never did.
    [Event(EvtRewriteUnavailable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Rewriter unavailable | reason={3} | caller={0} | provider={1} | model={2} | status={4} | ex_type={5} | ex_message={6}")]
    public void RewriteUnavailable(
        string caller, string provider, string model,
        string reason, int status, string ex_type, string ex_message)
    {
        if (IsEnabled())
            WriteEvent(EvtRewriteUnavailable, new object[]
            {
                caller, provider, model, reason, status, ex_type, ex_message,
            });
    }

    // A raw interactive failure while its incident is already open: the
    // RewriteUnavailable that opened it is the visible line, the repeats
    // stay here so one unreachable server is one incident, not one Warning
    // per paragraph.
    [Event(EvtRewriteAttemptFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "rewrite attempt failed | reason={3} | caller={0} | provider={1} | model={2} | status={4} | ex_type={5} | ex_message={6} | failures={7}")]
    public void RewriteAttemptFailed(
        string caller, string provider, string model,
        string reason, int status, string ex_type, string ex_message, int failures)
    {
        if (IsEnabled())
            WriteEvent(EvtRewriteAttemptFailed, new object[]
            {
                caller, provider, model, reason, status, ex_type, ex_message, failures,
            });
    }

    [Event(EvtRewriteRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "The rewriter answers again | caller={0} | provider={1} | failures={2}")]
    public void RewriteRecovered(string caller, string provider, int failures)
    {
        if (IsEnabled()) WriteEvent(EvtRewriteRecovered, new object[] { caller, provider, failures });
    }

    // ── Paragraph offer ─────────────────────────────────────────────────
    //
    // One settled line per paragraph request, whatever became of it.
    // outcome: offered, cancelled, no_result, gate_rejected, identity,
    // stale, failed. Edit counts only, never the paragraph or its rewrite.

    [Event(EvtParagraphOfferSettled,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "paragraph offer settled | outcome={0} | revision={1} | edits={2} | rejected_edits={3}")]
    public void ParagraphOfferSettled(string outcome, long revision, int edits, int rejected_edits)
    {
        if (IsEnabled()) WriteEvent(EvtParagraphOfferSettled, new object[] { outcome, revision, edits, rejected_edits });
    }

    [Event(EvtParagraphRewriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "A paragraph offer could not be prepared | ex_type={0} | ex_message={1}")]
    public void ParagraphRewriteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtParagraphRewriteFailed, ex_type, ex_message);
    }

}
