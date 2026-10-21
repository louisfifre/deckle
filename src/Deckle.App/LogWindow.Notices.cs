// LogWindow — notices above the list: activities whose verbosity control is
// off, and entries the inbound queue dropped.

using Deckle.Catalog;
using Deckle.Diagnostics;
using Deckle.Diagnostics.Logging;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    // Process lifetime, like the view filter: a closed notice stays closed
    // until Deckle restarts, unless another activity is turned off meanwhile.
    private static int _dismissedGateMask;

    private int _offGateMask;
    private int _droppedEntryCount;
    private bool _noticesDetached;

    private void AttachNotices()
    {
        LoggingSettingsService.Instance.Changed += OnLoggingSettingsChanged;
        RefreshGatesNotice();
    }

    // The settings service is a process singleton: without this, its event
    // would keep every closed window alive.
    private void DetachNotices()
    {
        _noticesDetached = true;
        LoggingSettingsService.Instance.Changed -= OnLoggingSettingsChanged;
    }

    // Raised on a timer or watcher thread once the settings file is written.
    private void OnLoggingSettingsChanged()
        => DispatcherQueue.TryEnqueueOrLog(RefreshGatesNotice, "LOGWIN", "gates notice");

    private void RefreshGatesNotice()
    {
        // A refresh queued just before Closed must not touch a closed window.
        if (_noticesDetached) return;

        int mask = 0;
        var offNames = new List<string>();
        foreach (OperationalLogActivity activity in Enum.GetValues<OperationalLogActivity>())
        {
            if (OperationalLogAdmission.IsEnabled(activity)) continue;
            mask |= 1 << (int)activity;
            offNames.Add(Loc.Get("LogWindow_Activity_" + activity));
        }
        _offGateMask = mask;

        if (mask == 0)
        {
            GatesInfoBar.IsOpen = false;
            return;
        }

        GatesInfoBar.Message = Loc.Format("LogWindow_GatesNotice_Format", string.Join(", ", offNames));
        GatesInfoBar.IsOpen = (mask & ~_dismissedGateMask) != 0;
    }

    private void OnGatesInfoBarCloseButtonClick(InfoBar sender, object args)
        => _dismissedGateMask |= _offGateMask;

    // Reopens a bar the person closed: new drops are new information.
    private void ReportDroppedEntries(int dropped)
    {
        _droppedEntryCount += dropped;
        DroppedEntriesInfoBar.Message = Loc.Format("LogWindow_DroppedNotice_Format", _droppedEntryCount);
        DroppedEntriesInfoBar.IsOpen = true;
    }

    private void ResetDroppedEntries()
    {
        _droppedEntryCount = 0;
        DroppedEntriesInfoBar.IsOpen = false;
    }
}
