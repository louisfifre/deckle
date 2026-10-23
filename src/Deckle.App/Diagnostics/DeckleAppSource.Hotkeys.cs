using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
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
           Message = "hotkey={0}")]
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

    // reason: chord_taken (win32_error 1409, another app owns the chord),
    // key_not_on_layout (0, the key is absent from the active layout) or
    // register_failed (any other code). chord=unbound when none is set.
    [Event(EvtHotkeyRegistrationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Cannot register the hotkey\naction={0} | chord={1} | reason={2} | win32_error={3}")]
    public void HotkeyRegistrationFailed(string action, string chord, string reason, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyRegistrationFailed, action, chord, reason, win32_error);
    }

    // One per press the app cannot act on. reason: engine_not_composed
    // (speech not provisioned; the HUD tells the person how to set it up).
    [Event(EvtHotkeyPressIgnored,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Dictation hotkey ignored")]
    public void HotkeyPressIgnored(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyPressIgnored, reason);
    }

    // win32_error 1409 = chord owned elsewhere; 0 = key absent from the
    // active keyboard layout.
    [Event(EvtHotkeyRegistrationFailedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "action={0} | id={1} | modifiers=0x{2:X} | scancode=0x{3:X} | win32_error={4}")]
    public void HotkeyRegistrationFailedDetail(string action, int id, uint modifiers, uint scancode, int win32_error)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyRegistrationFailedDetail, action, id, modifiers, scancode, win32_error);
    }

    [Event(EvtHotkeyToggle,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "Module switched from its hotkey")]
    public void HotkeyToggle()
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyToggle);
    }

    [Event(EvtHotkeyToggleDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Capture,
           Message = "toggle={0}")]
    public void HotkeyToggleDetail(string toggle)
    {
        if (IsEnabled()) WriteEvent(EvtHotkeyToggleDetail, toggle);
    }

}
