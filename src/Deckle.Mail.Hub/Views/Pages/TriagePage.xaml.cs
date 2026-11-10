using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Deckle.Mail.Hub;

// ── Triage, and Triage kept to a mailbox or an alias ──────────────────────────
//
// One page for the three, since an Inbox or alias page is Triage filtered:
// its rows, then its secondary information, under a breadcrumb (spec, An
// Inbox page). The row views live as long as the page and follow the session
// in place: a gesture greys its row where it is, nothing reorders, and the
// focus stays on the row the person is working on.
public sealed partial class TriagePage : Page
{
    private readonly ObservableCollection<SubjectRowView> _rows = [];
    private readonly ObservableCollection<InfoLineView> _infos = [];
    private IMailShell? _shell;
    private TriageSession? _session;
    private MailDestination? _destination;

    public TriagePage()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;
        InfoList.ItemsSource = _infos;
    }

    private MailFilter Filter => _destination?.Filter ?? MailFilter.All;

    // For the self-check: the view of one row, to open its menu of days.
    public SubjectRowView? RowView(string subjectId) => _rows.FirstOrDefault(r => r.Row.Id == subjectId);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not MailPageArgs args) return;

        // The page is cached: coming back to the same list keeps it as it was
        // left; another list, or another session, rebuilds it.
        bool same = ReferenceEquals(args.Shell.Session, _session) && args.Destination == _destination;
        if (!ReferenceEquals(args.Shell.Session, _session))
        {
            if (_session is not null) _session.Changed -= OnSessionChanged;
            args.Shell.Session.Changed += OnSessionChanged;
        }
        _shell = args.Shell;
        _session = args.Shell.Session;
        _destination = args.Destination;

        if (same)
        {
            Refresh();
            return;
        }
        _rows.Clear();
        _infos.Clear();
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
        BuildHeader();
        Refresh();
    }

    private void OnSessionChanged(SessionChange change)
    {
        if ((change & SessionChange.Mailboxes) != 0) BuildHeader();
        Refresh();
    }

    // ── Header ───────────────────────────────────────────────────────────────

    private void BuildHeader()
    {
        if (_session is null || _destination is null) return;
        Header.CrumbClicked -= OnCrumbClicked;
        Header.CrumbClicked += OnCrumbClicked;

        Mailbox? mailbox = _session.FindMailbox(_destination.MailboxId);
        if (_destination.Page == MailPage.Triage || mailbox is null)
        {
            Header.Visibility = Visibility.Collapsed;
            return;
        }

        Header.Visibility = Visibility.Visible;
        string mailboxLabel = MailNames.Mailbox(mailbox);
        var toMailbox = new HeaderCrumb(mailboxLabel, MailDestination.Mailbox(mailbox.Id));

        if (_destination.Page == MailPage.Alias
            && mailbox.Aliases.FirstOrDefault(a => a.Id == _destination.AliasId) is { } alias)
        {
            string aliasLabel = MailNames.Alias(alias, mailbox.NamesAliasesFromAddress);
            Header.SetTrail([toMailbox, new HeaderCrumb(aliasLabel, null)]);
            Header.SetDescription(alias.Address);
            Header.EnableRename(aliasLabel, MailNames.Alias(alias with { GivenName = null }, true),
                name => _session.RenameAlias(mailbox.Id, alias.Id, name));
            return;
        }

        if (mailbox.IsGroup)
        {
            // Inbox stands for the main address and is not renamed.
            Header.SetTrail([toMailbox, new HeaderCrumb(MailText.Get("Nav_Inbox"), null)]);
            Header.SetDescription(mailbox.Address);
            Header.DisableRename();
            return;
        }

        // A mailbox without aliases is a single item: its name is its title.
        Header.SetTitle(mailboxLabel);
        Header.SetDescription(mailboxLabel == mailbox.Address ? null : mailbox.Address);
        Header.EnableRename(mailboxLabel, mailbox.Address, name => _session.RenameMailbox(mailbox.Id, name));
    }

    private void OnCrumbClicked(MailDestination target) => _shell?.Go(target);

    // ── Rows, lines and the commands under them ──────────────────────────────

    private void Refresh()
    {
        if (_session is null || _destination is null) return;
        MailFilter filter = Filter;

        SyncRows(filter);
        SyncInfos(filter);

        bool noMailbox = !_session.HasMailbox;
        bool nothingWaiting = !noMailbox && _rows.Count == 0;
        EmptyPanel.Visibility = noMailbox || nothingWaiting ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = MailText.Get(noMailbox ? "Triage_NoMailbox" : "Triage_NothingWaiting");
        EmptySentence.Text = MailText.Get("Triage_NoMailbox_Sentence");
        EmptySentence.Visibility = noMailbox ? Visibility.Visible : Visibility.Collapsed;
        AddMailboxButton.Visibility = noMailbox ? Visibility.Visible : Visibility.Collapsed;
        RowsCard.Visibility = _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        InfoCaption.Visibility = _infos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoList.Visibility = _infos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        int setAside = _session.SetAsideCount(filter);
        SetAsideLink.Visibility = setAside > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetAsideText.Text = MailText.Format("Triage_SetAside_Format", setAside);
        // Under the rows when there is no line, after the lines otherwise.
        SetAsideLink.Margin = _infos.Count > 0 ? new Thickness(5, 4, 0, 0) : new Thickness(5, 20, 0, 0);

        // Done for today ends the whole day, so it stands on Triage alone.
        bool showDone = _destination.Page == MailPage.Triage && (_rows.Count > 0 || _infos.Count > 0);
        DoneRow.Visibility = showDone ? Visibility.Visible : Visibility.Collapsed;
        DoneButton.Style = (Style)Application.Current.Resources[
            _session.IsEveryRowTreated(filter) ? "AccentButtonStyle" : "DefaultButtonStyle"];
    }

    private void SyncRows(MailFilter filter)
    {
        List<TriageRow> rows = [.. _session!.RowsIn(filter)];
        var present = new HashSet<string>(rows.Select(r => r.Id), StringComparer.Ordinal);
        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!present.Contains(_rows[i].Row.Id) || !ReferenceEquals(_rows[i].Row, _session.FindRow(_rows[i].Row.Id)))
                _rows.RemoveAt(i);

        var shown = new HashSet<string>(_rows.Select(r => r.Row.Id), StringComparer.Ordinal);
        foreach (TriageRow row in rows)
            if (!shown.Contains(row.Id))
                _rows.Add(new SubjectRowView(_session, row));

        foreach (SubjectRowView view in _rows) view.Refresh();
    }

    private void SyncInfos(MailFilter filter)
    {
        List<InfoLine> lines = [.. _session!.InfoLinesIn(filter)];
        var present = new HashSet<string>(lines.Select(l => l.Id), StringComparer.Ordinal);
        for (int i = _infos.Count - 1; i >= 0; i--)
            if (!present.Contains(_infos[i].Line.Id)) _infos.RemoveAt(i);

        var shown = new HashSet<string>(_infos.Select(l => l.Line.Id), StringComparer.Ordinal);
        foreach (InfoLine line in lines)
            if (!shown.Contains(line.Id))
                _infos.Add(new InfoLineView(_session, line));
    }

    private void OnRowClick(object sender, ItemClickEventArgs e)
    {
        if (_session is null || e.ClickedItem is not SubjectRowView { Row: var row }) return;
        // A treated row opens nothing, and a click in the instant after a
        // gesture does nothing.
        if (row.IsTreated || !_session.AcceptsClick(row.Id)) return;
        _shell?.Go(MailDestination.Subject(row.Id));
    }

    private void OnInfoListFocusChanged(object sender, RoutedEventArgs e)
    {
        foreach (InfoLineView view in _infos)
            if (InfoList.ContainerFromItem(view) is ListViewItem item)
                view.SetContainerFocused(item.FocusState != FocusState.Unfocused);
    }

    private void OnDismissAllClick(object sender, RoutedEventArgs e)
    {
        int dismissed = _session?.DismissAllInfo(Filter) ?? 0;
        DeckleMailHubSource.Log.GestureApplied("dismiss_all", dismissed > 0);
    }

    private void OnSetAsideClick(object sender, RoutedEventArgs e)
        => _shell?.Go(MailDestination.Activity(ActivityPart.ByDeckle));

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        _session?.EndDay();
        DeckleMailHubSource.Log.GestureApplied("end_day", true);
    }

    private void OnAddMailboxClick(object sender, RoutedEventArgs e) => _shell?.Go(MailDestination.AddMailbox);
}
