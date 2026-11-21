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
           Message = "A message was shown on screen")]
    public void UserFeedbackEmitted(int severity, string title, string body, int role)
    {
        if (IsEnabled()) WriteEvent(EvtUserFeedbackEmitted, severity, title, body, role);
    }

    // Each time the person opens a window, created or already there:
    // milestone-tagged so log.jsonl says what was on screen. Only the
    // window's name: settings, log, folders, projects.
    [Event(EvtWindowOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Tags = ObservationTags.Milestone,
           Message = "A window was opened")]
    public void WindowOpened(string window)
    {
        if (IsEnabled()) WriteEvent(EvtWindowOpened, window);
    }

    // toggle is the TrayToggle id; the hotkey path also logs HotkeyToggle.
    [Event(EvtTrayToggleChanged,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Module switched")]
    public void TrayToggleChanged(string toggle, bool enabled)
    {
        if (IsEnabled()) WriteEvent(EvtTrayToggleChanged, toggle, enabled);
    }

    // The capture started from the tray menu wrote its images, but Explorer
    // did not open on them, the only sign the person would have had.
    [Event(EvtCaptureFolderOpenFailed,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The interface capture folder could not be opened; the images are saved in it\nex_type={0} | ex_message={1}")]
    public void CaptureFolderOpenFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtCaptureFolderOpenFailed, ex_type, ex_message);
    }

    // source: app-init for the boot apply, user for a change from Settings.
    [Event(EvtThemeApplied,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Theme,
           Message = "theme={0} | source={1} | windows={2}")]
    public void ThemeApplied(string theme, string source, int windows)
    {
        if (IsEnabled()) WriteEvent(EvtThemeApplied, theme, source, windows);
    }

    [Event(EvtThemeUnknown,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Theme,
           Message = "Unknown theme name, the system theme is used\nname={0}")]
    public void ThemeUnknown(string name)
    {
        if (IsEnabled()) WriteEvent(EvtThemeUnknown, name);
    }
}
