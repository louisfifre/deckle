using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Mail.Hub;

// ── Activity ──────────────────────────────────────────────────────────────────
//
// Newest first, grouped by day: in the person's part what they did, in the
// sorting's part each mail it set aside, one line each even several from one
// sender. Undo on a line brings its subject back to Triage; a line opens its
// subject. The page holds no search field: the title bar's searches all.
public sealed partial class ActivityPage : Page
{
    private IMailShell? _shell;
    private TriageSession? _session;
    private ActivityPart _part = ActivityPart.ByYou;
    private bool _choosingPart;

    public ActivityPage()
    {
        InitializeComponent();
        Header.SetTitle(MailText.Get("Nav_Activity"));
    }

    public ActivityPart Part => _part;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MailPageArgs args) return;
        if (!ReferenceEquals(args.Shell.Session, _session))
        {
            if (_session is not null) _session.Changed -= OnSessionChanged;
            args.Shell.Session.Changed += OnSessionChanged;
        }
        _shell = args.Shell;
        _session = args.Shell.Session;
        ShowPart(args.Destination.Part);
    }

    // Shows a part as if its tab were clicked; for the self-check too.
    public void ShowPart(ActivityPart part)
    {
        _part = part;
        _choosingPart = true;
        try { Parts.SelectedItem = part == ActivityPart.ByYou ? ByYouItem : ByDeckleItem; }
        finally { _choosingPart = false; }
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
        Build();
    }

    // Chooses a part through its tab's own selection pattern, the path a click
    // and Narrator both take; for the self-check, which must see what a
    // person's choice does and not what ShowPart sets.
    public void ChoosePart(ActivityPart part)
    {
        SelectorBarItem tab = part == ActivityPart.ByYou ? ByYouItem : ByDeckleItem;
        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(tab);
        if (peer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.SelectionItem)
            is Microsoft.UI.Xaml.Automation.Provider.ISelectionItemProvider selection)
            selection.Select();
    }

    private void OnPartChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_choosingPart) return;
        _part = ReferenceEquals(sender.SelectedItem, ByDeckleItem) ? ActivityPart.ByDeckle : ActivityPart.ByYou;
        Build();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & SessionChange.Activity) != 0) Build();
    }

    private void Build()
    {
        if (_session is null) return;
        DaysPanel.Children.Clear();
        IReadOnlyList<ActivityDay> days = _session.ActivityDays(_part);

        NoneText.Text = MailText.Get(_part == ActivityPart.ByYou ? "Activity_NoneByYou" : "Activity_NoneByDeckle");
        NoneText.Visibility = days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        bool first = true;
        foreach (ActivityDay day in days)
        {
            // A day is a section title: semibold, in the main ink, at the
            // edge of its list.
            DaysPanel.Children.Add(new TextBlock
            {
                Text = MailText.Heading(day.Day, _session.Today),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(1, first ? 16 : 28, 0, 8),
            });
            first = false;

            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = true,
                ItemContainerStyle = (Style)Resources["MailRowContainerStyle"],
            };
            ComponentMotion.Still(list);
            list.ItemClick += OnLineClick;
            foreach (ActivityEntry entry in day.Entries)
                list.Items.Add(new ActivityLineView(_session, entry));

            DaysPanel.Children.Add(new Border
            {
                Child = list,
                Padding = new Thickness(0, 4, 0, 4),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
                Style = (Style)Resources["CardBorderStyle"],
            });
        }
    }

    private void OnLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ActivityLineView line && _session?.FindSubject(line.Entry.SubjectId) is not null)
            _shell?.Go(MailDestination.Subject(line.Entry.SubjectId));
    }
}
