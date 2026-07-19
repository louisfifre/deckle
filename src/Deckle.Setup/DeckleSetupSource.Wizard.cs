using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Setup;

public sealed partial class DeckleSetupSource
{
    // ── Wizard lifecycle ──────────────────────────────────────────────────────

    [Event(EvtWizardOpening,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Opening the first-run setup wizard")]
    public void WizardOpening()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardOpening);
    }

    [Event(EvtWizardOpeningDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "first run gate | natives_installed={0} | default_model_installed={1}")]
    public void WizardOpeningDetail(bool natives_installed, bool default_model_installed)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardOpeningDetail, natives_installed, default_model_installed);
    }

    [Event(EvtWizardCancelled,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup wizard was cancelled")]
    public void WizardCancelled()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWizardCancelled);
    }

    [Event(EvtWindowOpened,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup window opened")]
    public void WindowOpened()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowOpened);
    }

    [Event(EvtWindowClosing,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup window is closing")]
    public void WindowClosing()
    {
        if (IsEnabled(EventLevel.Informational, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosing);
    }

    [Event(EvtWindowClosingDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "setup window closing | success={0}")]
    public void WindowClosingDetail(bool success)
    {
        if (IsEnabled(EventLevel.Verbose, (EventKeywords)Keywords.Lifecycle))
            WriteEvent(EvtWindowClosingDetail, success);
    }

}
