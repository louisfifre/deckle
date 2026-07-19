using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Llm;

public sealed partial class DeckleLlmSource
{
    // ── Ollama /api/ps polling ──────────────────────────────────────────

    [Event(EvtPsProbeUnreachable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The model status probe could not be reached — the model may have crashed")]
    public void PsProbeUnreachable()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeUnreachable);
    }

    [Event(EvtPsProbeUnreachableDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe unreachable | http={0}")]
    public void PsProbeUnreachableDetail(int http_status)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeUnreachableDetail, http_status);
    }

    // Constant hint (no resident model, request may be stuck) lived in the old
    // k=v message; the method takes no args, so the milestone carries no detail
    // and no Verbose mirror is needed.
    [Event(EvtPsProbeEmpty,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe empty | resident_models=0")]
    public void PsProbeEmpty()
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeEmpty);
    }

    [Event(EvtOllamaBusy,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "Ollama is busy with another model")]
    public void OllamaBusy()
    {
        if (IsEnabled()) WriteEvent(EvtOllamaBusy);
    }

    [Event(EvtOllamaBusyDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ollama busy | name={0} | vram_gb={1:F1} | unload={2} | waited_sec={3:F0} | cap_min={4:F0}")]
    public void OllamaBusyDetail(string name, double vram_gb, string unload_suffix, double waited_seconds, double cap_minutes)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtOllamaBusyDetail, name, vram_gb, unload_suffix, waited_seconds, cap_minutes);
    }

    [Event(EvtPsProbeFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "The model status probe failed")]
    public void PsProbeFailed()
    {
        if (IsEnabled()) WriteEvent(EvtPsProbeFailed);
    }

    [Event(EvtPsProbeFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Heartbeat,
           Message = "ps probe failed | error={0} | message={1}")]
    public void PsProbeFailedDetail(string ex_type, string message)
    {
        if (OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Heartbeat))
            WriteEvent(EvtPsProbeFailedDetail, ex_type, message);
    }

}
