using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;

namespace Deckle.Paste.Views;

// The inset's list: what was pasted since the window opened, newest first,
// and the saving sign above it while a save runs. PasteHistory keeps the
// rows' order and bound; the rows here mirror it with their words and
// thumbnails. Nothing is drawn as a status line: each result is announced to
// Narrator instead, as the paste zone's notification.
public sealed partial class PasteWindow
{
    private readonly PasteHistory _history = new();
    private readonly ObservableCollection<PasteRow> _rows = [];

    private void AttachList()
    {
        PasteList.ItemsSource = _rows;
        // In memory only: emptied with the window, thumbnails included.
        Closed += (_, _) => ClearList();
    }

    // One paste's rows, put at the top, and the list scrolled back up so
    // they show. The thumbnail goes to the paste's image row, if it has one.
    private void AddPaste(IReadOnlyList<PasteEntry> paste, ImageSource? thumbnail = null)
    {
        if (paste.Count == 0) return;
        _history.Add(paste);
        for (int i = paste.Count - 1; i >= 0; i--)
            _rows.Insert(0, new PasteRow(paste[i], thumbnail));
        while (_rows.Count > _history.Entries.Count) _rows.RemoveAt(_rows.Count - 1);

        ShowPlaceholderIfEmpty();
        ListScroller.ChangeView(null, 0, null, disableAnimation: true);
        Announce(paste);
    }

    private void ClearList()
    {
        _history.Clear();
        _rows.Clear();
        ShowSaving(false);
    }

    private void ShowSaving(bool saving)
    {
        SavingRing.IsActive = saving;
        SavingRow.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
        if (saving) ListScroller.ChangeView(null, 0, null, disableAnimation: true);
        ShowPlaceholderIfEmpty();
    }

    // The field's own placeholder, alone in an empty inset; gone as soon as
    // the inset shows anything.
    private void ShowPlaceholderIfEmpty()
    {
        bool empty = _rows.Count == 0 && SavingRow.Visibility == Visibility.Collapsed;
        PasteZone.PlaceholderText = empty ? PasteText.Get("Paste_Zone_Placeholder") : "";
    }

    // What Narrator says of one paste: its problem and reason, the saved
    // file's name, or how many items were saved.
    private void Announce(IReadOnlyList<PasteEntry> paste)
    {
        PasteRow first = _rows[0];
        int saved = paste.Count(e => !e.IsProblem);
        string spoken = first.Entry.IsProblem ? first.Spoken
            : saved == 1 ? PasteText.Format("Paste_Announce_Saved_Format", first.Title)
            : PasteText.Format("Paste_Announce_SavedMany_Format", saved);

        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(PasteZone)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(PasteZone);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            spoken,
            "PasteResult");
    }
}
