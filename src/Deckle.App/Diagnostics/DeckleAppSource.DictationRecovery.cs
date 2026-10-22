using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Dictation recovery at launch ────────────────────────────────────
    //
    // Dictated text is personal data: no event here carries text, only counts.

    [Event(EvtDictationRecoveryFound,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A dictation interrupted by an earlier run was found")]
    public void DictationRecoveryFound()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryFound);
    }

    [Event(EvtDictationRecovered,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interrupted dictation was put back on the clipboard")]
    public void DictationRecovered()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecovered);
    }

    [Event(EvtDictationRecoveredDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "dictation recovered | chars={0}")]
    public void DictationRecoveredDetail(int chars)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveredDetail, chars);
    }

    // reason: unreadable (DPAPI or the disk refused the file) or empty (it
    // unsealed to blank text). deleted=false means the same warning returns
    // at the next launch; the store logs the delete failure's cause.
    [Event(EvtDictationRecoveryUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interrupted dictation could not be recovered and was discarded | reason={0} | deleted={1}")]
    public void DictationRecoveryUnreadable(string reason, bool deleted)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryUnreadable, reason, deleted);
    }

    // reason comes from ClipboardWriteResult.Status; the clipboard writer
    // returns no Win32 error code, so the status word is the whole cause.
    [Event(EvtDictationRecoveryClipboardFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interrupted dictation could not be copied to the clipboard and is kept for the next launch | reason={0} | chars={1}")]
    public void DictationRecoveryClipboardFailed(string reason, int chars)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryClipboardFailed, reason, chars);
    }

    // The text reached the clipboard but its file could not be removed, so
    // the next launch puts it back on the clipboard a second time.
    [Event(EvtDictationRecoveryKept,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The recovered dictation stays on disk and will be restored again at the next launch | reason={0}")]
    public void DictationRecoveryKept(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryKept, reason);
    }
}
