using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // ── Hotkey (App-side observer) ──────────────────────────────────────

    [Event(EvtHotkeyStart,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Dictation started")]
    public void HotkeyStart()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStart);
    }

    [Event(EvtHotkeyStartDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "dictation started | hotkey={0}")]
    public void HotkeyStartDetail(string hotkey_label)
    {
        if (!OperationalLogAdmission.IsDetailEnabled(
                OperationalLogActivity.Transcription, this,
                EventLevel.Verbose, (EventKeywords)Keywords.Capture)) return;
        WriteEvent(EvtHotkeyStartDetail, hotkey_label);
    }

    [Event(EvtHotkeyStop,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Recording stop requested")]
    public void HotkeyStop()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyStop);
    }

    [Event(EvtHotkeyNoProfile,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "A rewrite hotkey was pressed with no profile bound")]
    public void HotkeyNoProfile()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyNoProfile);
    }

    [Event(EvtHotkeyNoProfileDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "hotkey ignored, no profile bound | hotkey={0}")]
    public void HotkeyNoProfileDetail(string hotkey_name)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyNoProfileDetail, hotkey_name);
    }

}
