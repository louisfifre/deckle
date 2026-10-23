using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Transcription;

public sealed partial class DeckleWhispSource
{
    // ── Dictation recovery ──────────────────────────────────────────────
    //
    // The recovery file holds dictated text, which is personal data: no event
    // here carries text, only the exception type and the system's message,
    // which names at most the file path. The write runs at every utterance,
    // so a failing disk is one incident: the first failure warns, repeats
    // stay Verbose, the next successful write closes it.

    [Event(EvtDictationRecoveryWriteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Saving the dictation recovery copy failed, the dictation goes on without it | ex_type={0} | ex_message={1}")]
    public void DictationRecoveryWriteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryWriteFailed, ex_type, ex_message);
    }

    [Event(EvtDictationRecoveryWriteRetryFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "recovery write failed again | ex_type={0} | ex_message={1}")]
    public void DictationRecoveryWriteRetryFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryWriteRetryFailed, ex_type, ex_message);
    }

    [Event(EvtDictationRecoveryWriteRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Tags = ObservationTags.Milestone,
           Message = "The dictation recovery copy is saved again | failures={0}")]
    public void DictationRecoveryWriteRecovered(int failures)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryWriteRecovered, failures);
    }

    // The caller reports the Unreadable outcome to the user; this keeps why.
    [Event(EvtDictationRecoveryReadFailed,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "recovery read failed | ex_type={0} | ex_message={1}")]
    public void DictationRecoveryReadFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryReadFailed, ex_type, ex_message);
    }

    [Event(EvtDictationRecoveryDeleteFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Pipeline,
           Message = "Deleting the dictation recovery copy failed | ex_type={0} | ex_message={1}")]
    public void DictationRecoveryDeleteFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryDeleteFailed, ex_type, ex_message);
    }
}
