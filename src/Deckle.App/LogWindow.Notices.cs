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

    private void ResetDroppedEntries()
    {
        _droppedEntryCount = 0;
        DroppedEntriesInfoBar.IsOpen = false;
    }
}
