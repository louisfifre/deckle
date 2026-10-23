using System.Diagnostics.Tracing;
using Deckle.Diagnostics;

namespace Deckle.Settings;

public sealed partial class DeckleSettingsSource
{
    // ── General page (setup wizard) ─────────────────────────────────────

    [Event(EvtSetupWizardHookNotWired,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup button in Settings did nothing because the app did not connect it")]
    public void SetupWizardHookNotWired()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWizardHookNotWired);
    }

    [Event(EvtSetupWindowOpenedFromSettings,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Setup window opened from Settings")]
    public void SetupWindowOpenedFromSettings()
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenedFromSettings);
    }

    [Event(EvtSetupWindowOpenFailed,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The setup window could not be opened\nex_type={0} | ex_message={1}")]
    public void SetupWindowOpenFailed(string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtSetupWindowOpenFailed, ex_type, ex_message);
    }

    // One event per measure, not per file: the data folder can hold thousands
    // of files and the live app rotates logs during the walk.
    [Event(EvtDataSizeFilesSkippedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "count={0} | ex_type={1} | ex_message={2}")]
    public void DataSizeFilesSkippedDetail(int count, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtDataSizeFilesSkippedDetail, count, ex_type, ex_message);
    }

    // ── SettingsWindow navigation ───────────────────────────────────────

    [Event(EvtNavSelectionChanged,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item={0}")]
    public void NavSelectionChanged(string item_content)
    {
        if (IsEnabled()) WriteEvent(EvtNavSelectionChanged, item_content);
    }

    [Event(EvtNavSelectionIgnored,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "reason={0}")]
    public void NavSelectionIgnored(string reason)
    {
        if (IsEnabled()) WriteEvent(EvtNavSelectionIgnored, reason);
    }

    [Event(EvtNavImpossibleNoTag,
           Level = EventLevel.Warning,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "A Settings menu item names no page, so selecting it does nothing")]
    public void NavImpossibleNoTag()
    {
        if (IsEnabled()) WriteEvent(EvtNavImpossibleNoTag);
    }

    [Event(EvtNavImpossibleNoTagDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "item_content={0}")]
    public void NavImpossibleNoTagDetail(string item_content)
    {
        if (IsEnabled()) WriteEvent(EvtNavImpossibleNoTagDetail, item_content);
    }

    [Event(EvtNavFailedTypeNotFound,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The page a Settings menu item names does not exist, so it did not open")]
    public void NavFailedTypeNotFound()
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedTypeNotFound);
    }

    [Event(EvtNavFailedTypeNotFoundDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "tag={0}")]
    public void NavFailedTypeNotFoundDetail(string tag)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedTypeNotFoundDetail, tag);
    }

    [Event(EvtNavSkippedAlreadyCurrent,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page_name={0}")]
    public void NavSkippedAlreadyCurrent(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavSkippedAlreadyCurrent, page_name);
    }

    [Event(EvtNavStarted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation started")]
    public void NavStarted()
    {
        if (IsEnabled()) WriteEvent(EvtNavStarted);
    }

    [Event(EvtNavStartedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0}")]
    public void NavStartedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavStartedDetail, page_name);
    }

    [Event(EvtNavFailedFrameRejected,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "The Settings window refused to open a page")]
    public void NavFailedFrameRejected()
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedFrameRejected);
    }

    [Event(EvtNavFailedFrameRejectedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page_name={0}")]
    public void NavFailedFrameRejectedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedFrameRejectedDetail, page_name);
    }

    [Event(EvtNavCompleted,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Navigation completed")]
    public void NavCompleted()
    {
        if (IsEnabled()) WriteEvent(EvtNavCompleted);
    }

    [Event(EvtNavCompletedDetail,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0}")]
    public void NavCompletedDetail(string page_name)
    {
        if (IsEnabled()) WriteEvent(EvtNavCompletedDetail, page_name);
    }

    [Event(EvtNavTiming,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0} | duration_ms={1}")]
    public void NavTiming(string page_name, long duration_ms)
    {
        if (IsEnabled()) WriteEvent(EvtNavTiming, page_name, duration_ms);
    }

    // Time from nav-start (NavClock) to the destination page's first Loaded,
    // which covers the work (ViewModel.Load + control sync) Navigate returns before.
    [Event(EvtPageReady,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "page={0} | ready_ms={1}")]
    public void PageReady(string page_name, long ready_ms)
    {
        if (IsEnabled()) WriteEvent(EvtPageReady, page_name, ready_ms);
    }

    [Event(EvtNavFailedThrew,
           Level = EventLevel.Error,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Opening a Settings page failed on an error\npage={0} | ex_type={1} | ex_message={2}")]
    public void NavFailedThrew(string page, string ex_type, string ex_message)
    {
        if (IsEnabled()) WriteEvent(EvtNavFailedThrew, page, ex_type, ex_message);
    }

    // Verbose: a raw stack trace is opaque detail with no milestone value. It
    // follows the NavFailedThrew milestone.
    [Event(EvtNavStackTrace,
           Level = EventLevel.Verbose,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "stack={0}")]
    public void NavStackTrace(string stack)
    {
        if (IsEnabled()) WriteEvent(EvtNavStackTrace, stack);
    }

    [Event(EvtOpenLogsFromFooter,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Open logs from footer")]
    public void OpenLogsFromFooter()
    {
        if (IsEnabled()) WriteEvent(EvtOpenLogsFromFooter);
    }

    [Event(EvtOpenPlaygroundFromFooter,
           Level = EventLevel.Informational,
           Keywords = (EventKeywords)Keywords.Lifecycle,
           Message = "Open playground from footer")]
    public void OpenPlaygroundFromFooter()
    {
        if (IsEnabled()) WriteEvent(EvtOpenPlaygroundFromFooter);
    }

}
