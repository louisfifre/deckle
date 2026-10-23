using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── Ollama /api/ps polling ──────────────────────────────────────────
    //
    // Every per-tick line answers to the Transcription activity, the same
    // gate that decides whether the probe runs at all. No Warning lives
    // here: the probe is optional detail, and the rewrite milestones carry
    // the outcome a person acts on.

    [Event(EvtPsProbeRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "status={0}")]
    public void PsProbeRejectedDetail(int status)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeRejectedDetail, status);
    }

    // Ollama reports no resident model; elapsed_ms is how long the rewrite has
    // waited so far, as in PsProbeBusyDetail.
    [Event(EvtPsProbeEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "elapsed_ms={0}")]
    public void PsProbeEmpty(long elapsed_ms)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeEmpty, elapsed_ms);
    }

    // unloads_in_s is Ollama's keep_alive countdown for the resident model,
    // 0 when it reports none or it has run out; elapsed_ms is how long the
    // rewrite has waited so far, cap_s the deadline it waits against.
    [Event(EvtPsProbeBusyDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "model={0} | vram_gb={1:F1} | unloads_in_s={2:F0} | elapsed_ms={3} | cap_s={4:F0}")]
    public void PsProbeBusyDetail(string model, double vram_gb, double unloads_in_s, long elapsed_ms, double cap_s)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeBusyDetail, new object[] { model, vram_gb, unloads_in_s, elapsed_ms, cap_s });
    }

    [Event(EvtPsProbeErrorDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ex_type={0} | ex_message={1}")]
    public void PsProbeErrorDetail(string ex_type, string ex_message)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeErrorDetail, ex_type, ex_message);
    }

    // The probe task itself faulted outside its per-tick catch. At most one
    // per rewrite, and ungoverned on purpose: the detail gate may have
    // closed mid-request, and a swallowed fault must still leave a trace.
    [Event(EvtPsProbeFaulted,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ex_type={0} | ex_message={1}")]
    public void PsProbeFaulted(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeFaulted, ex_type, ex_message);
    }

}
