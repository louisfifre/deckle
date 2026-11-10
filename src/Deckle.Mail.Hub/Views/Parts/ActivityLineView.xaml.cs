using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Deckle.Mail.Hub;

// A line of Activity. In the person's part, what happened is written in
// words, as on a treated row; in the sorting's part, it is a framed word
// among newsletter, promotion, receipt and no action needed.
public sealed partial class ActivityLineView : UserControl
{
    private readonly TriageSession _session;

    public ActivityLineView(TriageSession session, ActivityEntry entry)
    {
        InitializeComponent();
        _session = session;
        Entry = entry;
        Task.UndoRequested += OnUndo;

        TimeText.Text = MailDates.Time(entry.At);
        SentenceText.Text = entry.Sentence;
        SentenceText.Language = entry.Language;

        if (entry.Undone)
        {
            VisualStateManager.GoToState(this, "Undone", false);
            Task.ShowUndone(MailText.Get("Activity_BackInTriage"));
        }
        else if (entry.Gesture is { } gesture)
        {
            Task.ShowResult(MailText.Result(gesture, session.Today), waitsForMailbox: false, waitingFor: null);
        }
        else if (entry.Reason is { } reason)
        {
            Task.ShowReason(MailText.Reason(reason));
        }
        Task.SetUndoName(MailText.Format("Row_UndoName_Format", entry.Sentence));
        AutomationProperties.SetName(this, MailText.Format("Join_Comma_Format", TimeText.Text, entry.Sentence));
    }

    public ActivityEntry Entry { get; }

    private void OnUndo()
    {
        bool applied = _session.UndoActivity(Entry.Id);
        DeckleMailHubSource.Log.GestureApplied("undo_activity", applied);
    }
}
