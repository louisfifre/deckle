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

    [Event(EvtDictationRecoveryUnreadable,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interrupted dictation could not be read and was discarded")]
    public void DictationRecoveryUnreadable()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryUnreadable);
    }

    [Event(EvtDictationRecoveryClipboardFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interrupted dictation could not be copied to the clipboard and is kept for the next launch")]
    public void DictationRecoveryClipboardFailed()
    {
        if (IsEnabled()) WriteEvent(EvtDictationRecoveryClipboardFailed);
    }
}
