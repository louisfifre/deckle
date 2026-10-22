using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.App;

public sealed partial class DeckleAppSource
{
    // HudFeedbackSink matches this event by name. Severity 0/1/2 =
    // Info/Warning/Error, role 0/1 = Replacement/Overlay.
    [Event(EvtUserFeedbackEmitted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Push,
           Message = "{1}: {2}")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // One event for any secondary window step that failed without breaking
    // the window. window: settings, log, playground. operation names the step
    // (placement_restore, placement_save, placement_load...); reason is
    // exception when ex_type and ex_message carry the cause.
    [Event(EvtWindowOperationFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Windowing,
           Message = "A window step failed | window={0} | operation={1} | reason={2} | {3}: {4}")]
    public void WindowOperationFailed(string window, string operation, string reason, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWindowOperationFailed, window, operation, reason, ex_type, ex_message);
    }

    // Once per window per process: later opens re-activate the same window.
    [Event(EvtWindowCreated,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Journal,
           Message = "Window created | window={0}")]
    public void WindowCreated(string window)
    {
        if (IsEnabled()) WriteEvent(EvtWindowCreated, window);
    }

    // The window does not open; the exception goes on to the crash net.
    [Event(EvtWindowOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Cannot open the window | window={0} | {1}: {2}")]
    public void WindowOpenFailed(string window, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtWindowOpenFailed, window, ex_type, ex_message);
    }

    // The file picker or the queueing failed: no file is transcribed and
    // nothing tells the person, so this Warning is the only trace.
    [Event(EvtFileTranscriptionEntryFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Cannot start file transcription | {0}: {1}")]
    public void FileTranscriptionEntryFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtFileTranscriptionEntryFailed, ex_type, ex_message);
    }

    // toggle is the TrayToggle id; the hotkey path also logs HotkeyToggle.
    [Event(EvtTrayToggleChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Module switched | toggle={0} | enabled={1}")]
    public void TrayToggleChanged(string toggle, bool enabled)
    {
        if (IsEnabled()) WriteEvent(EvtTrayToggleChanged, toggle, enabled);
    }

    // source: app-init for the boot apply, user for a change from Settings.
    [Event(EvtThemeApplied,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Theme,
           Message = "theme applied | theme={0} | source={1} | windows={2}")]
    public void ThemeApplied(string theme, string source, int windows)
    {
        if (IsEnabled()) WriteEvent(EvtThemeApplied, theme, source, windows);
    }

    [Event(EvtThemeUnknown,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Theme,
           Message = "Unknown theme name, the system theme is used | name={0}")]
    public void ThemeUnknown(string name)
    {
        if (IsEnabled()) WriteEvent(EvtThemeUnknown, name);
    }
}
