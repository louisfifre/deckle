using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Dictation recovery ──────────────────────────────────────────────
    //
    // The recovery file holds dictated text, which is personal data: no event
    // here carries text, only the exception type.

    [Event(EvtDictationRecoveryWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Saving the dictation recovery copy failed. The dictation goes on without it.")]
    public void DictationRecoveryWriteFailed()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryWriteFailed);
    }

    [Event(EvtDictationRecoveryWriteFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "recovery write failed | ex_type={0}")]
    public void DictationRecoveryWriteFailedDetail(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryWriteFailedDetail, ex_type);
    }

    [Event(EvtDictationRecoveryDeleteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Deleting the dictation recovery copy failed.")]
    public void DictationRecoveryDeleteFailed()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryDeleteFailed);
    }

    [Event(EvtDictationRecoveryDeleteFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "recovery delete failed | ex_type={0}")]
    public void DictationRecoveryDeleteFailedDetail(string ex_type)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryDeleteFailedDetail, ex_type);
    }
}
