// LogWindow — notice above the list: entries the inbound queue dropped.

using Deckle.Catalog;
using Deckle.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.App;

public sealed partial class LogWindow : Window, ILogWindowSink
{
    private int _droppedEntryCount;

    // Reopens a bar the person closed: new drops are new information.
    private void ReportDroppedEntries(int dropped)
    {
        _droppedEntryCount += dropped;
        DroppedEntriesInfoBar.Message = Loc.Format("LogWindow_DroppedNotice_Format", _droppedEntryCount);
        DroppedEntriesInfoBar.IsOpen = true;
    }

    // The App hands in the action (it owns the Settings window); a window
    // built without it shows the bar and the button does nothing.
    private void OnDroppedEntriesSettingsClick(object sender, RoutedEventArgs e)
        => OnShowSettingsRequested?.Invoke();

    private void ResetDroppedEntries()
    {
        _droppedEntryCount = 0;
        DroppedEntriesInfoBar.IsOpen = false;
    }
}
