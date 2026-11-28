using Deckle.Core;
using Deckle.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Deckle.Browser.Views;

// ─── Browser window: the gestures as the self-check presses them ─────────────
//
// The method is the one Folders uses (FoldersWindow.Gestures): a gesture is
// pressed through the control's own UI Automation peer, the path a screen
// reader takes and the one a click ends in. A button is invoked, a text field
// takes its value through its value pattern, a menu is opened as the pointer
// opens it and its entry invoked, a dialog's button is the template's own
// button. Nothing here sets what a gesture changes: the window's handlers do,
// and the walk then reads what the window shows.
//
// What no peer can do is said at each method. The window is shown with
// WS_EX_NOACTIVATE and never takes the keyboard (the run happens while the
// maintainer types), so a key is not pressed: Enter and Escape in the address
// field run the handler's own commit and cancel, after the text was typed
// through the field's value pattern. A routed pointer event cannot be built
// outside the framework, so the middle click runs the handler's own close,
// and a drag of a tab moves the list's items as the ListView does while it
// reorders them, then runs the handler its DragItemsCompleted ends in.
//
// A flyout or a menu opened for the review is drawn inside the window
// (ShouldConstrainToRootBounds), so the off-screen window's capture sees it
// and no popup window reaches a monitor.
public sealed partial class BrowserWindow
{
    /// <summary>What a menu shows of one entry, as the walk reads it.</summary>
    public sealed record ReviewMenuEntry(string AutomationId, string Text, bool IsEnabled, bool Drawn);

    // ── The address field ────────────────────────────────────────────────

    /// <summary>The field takes the focus and the text through its value
    /// pattern, as typing leaves it. False when the field does not hold
    /// the text.</summary>
    public bool TypeAddressForReview(string text)
    {
        if (AddressRow.Visibility != Visibility.Visible) return false;
        AddressBox.Focus(FocusState.Programmatic);
        if (PeerOf(AddressBox)?.GetPattern(PatternInterface.Value) is not IValueProvider value) return false;
        value.SetValue(text);
        return AddressBox.Text == text;
    }

    /// <summary>Enter in the field. Not a key: the handler's own commit.</summary>
    public void PressEnterInAddressForReview() => CommitAddress();

    /// <summary>Escape in the field. Not a key: the handler's own cancel.</summary>
    public void PressEscapeInAddressForReview() => CancelAddress();

    /// <summary>The text the address field shows.</summary>
    public string AddressTextForReview => AddressBox.Text;

    /// <summary>Whether the address field has the focus.</summary>
    public bool AddressHasFocusForReview => AddressBox.FocusState != FocusState.Unfocused;

    // ── The button row ───────────────────────────────────────────────────

    public bool ClickBackForReview() => InvokeByAutomation(BackButton);
    public bool ClickForwardForReview() => InvokeByAutomation(ForwardButton);
    public bool ClickReloadForReview() => InvokeByAutomation(ReloadButton);

    public bool BackEnabledForReview => BackButton.IsEnabled;
    public bool ForwardEnabledForReview => ForwardButton.IsEnabled;

    /// <summary>The panel's own button (the first of the row, or of the rail's column).</summary>
    public bool ClickPaneToggleForReview() => InvokeByAutomation(PaneToggleButton);

    /// <summary>The button a narrow window draws over the page.</summary>
    public bool ClickNarrowToggleForReview() =>
        NarrowToggleButton.Visibility == Visibility.Visible && InvokeByAutomation(NarrowToggleButton);

    public bool PaneOpenForReview => _pane.IsOpen;

    /// <summary>True while the panel hides behind the page: a narrow window.</summary>
    public bool PaneOverPageForReview => _pane.IsOverPage;

    public bool NarrowToggleShownForReview => NarrowToggleButton.Visibility == Visibility.Visible;

    /// <summary>Sizes the window as a person drags its edge, in device-independent pixels.</summary>
    public void ResizeForReview(int widthDips, int heightDips)
    {
        double scale = Dpi.ScaleOf(NativeMethods.GetDpiForWindow(_hwnd));
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(widthDips * scale), (int)(heightDips * scale)));
    }

    // ── The tabs and the pinned tiles ────────────────────────────────────

    public bool ClickNewTabForReview() => InvokeByAutomation(NewTabButton);

    /// <summary>The tab's own close button, shown as the pointer over the
    /// line shows it, invoked as a click presses it.</summary>
    public bool ClickCloseButtonOfTabForReview(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel) || TabList.ContainerFromItem(panel) is not DependencyObject container) return false;
        panel.IsHovered = true;
        Button? close = FindAll<Button>(container).FirstOrDefault(b => ReferenceEquals(b.Tag, panel));
        bool pressed = close is not null && close.Visibility == Visibility.Visible && InvokeByAutomation(close);
        panel.IsHovered = false;
        return pressed;
    }

    /// <summary>A middle click on the tab's line. Not a pointer event: the
    /// handler's own close.</summary>
    public bool MiddleClickTabForReview(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return false;
        CloseByMiddleClick(panel);
        return true;
    }

    /// <summary>Drags a tab to another place in the list. Not a pointer drag:
    /// the list's items are moved as the ListView moves them while it reorders,
    /// then the handler the end of its drag runs.</summary>
    public bool DragTabForReview(Guid id, int toIndex)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return false;
        int from = _tabItems.IndexOf(panel);
        if (from < 0 || toIndex < 0 || toIndex >= _tabItems.Count) return false;
        _syncing++;
        _tabItems.Move(from, toIndex);
        _syncing--;
        FollowTabOrder([panel]);
        return true;
    }

    /// <summary>The tab's own context menu (a right click on its line), its entries once drawn.</summary>
    public Task<IReadOnlyList<ReviewMenuEntry>> OpenTabMenuForReviewAsync(Guid id) => OpenContextMenuAsync(id, TabList);

    /// <summary>The pinned tile's context menu, its entries once drawn.</summary>
    public Task<IReadOnlyList<ReviewMenuEntry>> OpenTileMenuForReviewAsync(Guid id) => OpenContextMenuAsync(id, PinnedGrid);

    private async Task<IReadOnlyList<ReviewMenuEntry>> OpenContextMenuAsync(Guid id, DependencyObject list)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return [];
        Grid? line = FindAll<Grid>(list).FirstOrDefault(g => ReferenceEquals(g.DataContext, panel) && g.ContextFlyout is not null);
        if (line?.ContextFlyout is not FlyoutBase flyout) return [];
        CloseMenusForReview();
        flyout.ShouldConstrainToRootBounds = true;
        flyout.ShowAt(line);
        MenuFlyoutPresenter? presenter = null;
        await WaitForAsync(() => (presenter = OpenMenuPresenters().FirstOrDefault()) is { IsLoaded: true });
        return presenter is null ? [] : Entries(presenter);
    }

    /// <summary>An entry of the open menu, chosen as a click chooses it.</summary>
    public bool ChooseMenuEntryForReview(string automationId) =>
        OpenMenuPresenters()
            .SelectMany(p => p.Items.OfType<MenuFlyoutItemBase>())
            .FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == automationId) is { } entry
        && InvokeByAutomation(entry);

    public void CloseMenusForReview()
    {
        foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in OpenPopups())
            if (popup.Child is not null && FindAll<MenuFlyoutPresenter>(popup.Child).Any()) popup.IsOpen = false;
    }

    /// <summary>A pinned tile clicked: the item container's own invoke, which
    /// raises the grid's ItemInvoked as a click does.</summary>
    public bool ClickPinnedTileForReview(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return false;
        ItemContainer? tile = FindAll<ItemContainer>(PinnedGrid).FirstOrDefault(c => ReferenceEquals(c.DataContext, panel));
        return tile is not null && InvokeByAutomation(tile);
    }

    /// <summary>Adds a pinned site that has not started, as one restored from
    /// the session is at startup: an entry with no web view until its tile is
    /// first clicked. The selection stays where it was. Returns its id.</summary>
    public Guid AddPinnedEntryForReview(Uri uri)
    {
        Guid? before = _session.SelectedId;
        BrowserItem item = _session.OpenTab(uri.AbsoluteUri);
        _session.Pin(item.Id);
        if (before is { } keep) _session.Select(keep);
        PanelItem panel = Register(item);
        _pinnedItems.Add(panel);
        SyncSelectionUi();
        return item.Id;
    }

    /// <summary>Waits for the item's web view to answer a script: what a wake
    /// or a first load costs once its tile was clicked.</summary>
    public async Task WaitAnswersForReviewAsync(Guid id)
    {
        if (!_byId.TryGetValue(id, out PanelItem? panel)) return;
        await panel.Loaded.Task;
        await panel.View!.CoreWebView2.ExecuteScriptAsync("1");
    }

    /// <summary>Whether the item has a web view started and not suspended.</summary>
    public bool IsAwakeForReview(Guid id) =>
        _byId.TryGetValue(id, out PanelItem? panel) && panel.View?.CoreWebView2 is { IsSuspended: false };

    public bool HasWebViewForReview(Guid id) => _byId.TryGetValue(id, out PanelItem? panel) && panel.View?.CoreWebView2 is not null;

    /// <summary>The tab lines' items, top to bottom.</summary>
    public IReadOnlyList<Guid> TabOrderForReview => [.. _tabItems.Select(p => p.Item.Id)];

    public IReadOnlyList<Guid> PinnedOrderForReview => [.. _pinnedItems.Select(p => p.Item.Id)];

    /// <summary>What the shown item's title says, for the walk's comparison with the page's.</summary>
    public string ShownTitleForReview => _shown?.Title ?? "";

    // ── The foot and the address row's buttons ───────────────────────────

    /// <summary>The downloads button, invoked as a click: it opens its flyout.</summary>
    public bool ClickDownloadsForReview()
    {
        DownloadsFlyout.ShouldConstrainToRootBounds = true;
        return InvokeByAutomation(DownloadsButton);
    }

    public bool DownloadsFlyoutOpenForReview => DownloadsFlyout.IsOpen;

    /// <summary>The site-settings icon, invoked as a click: it opens its flyout.</summary>
    public bool ClickSiteSettingsForReview()
    {
        SiteSettingsFlyout.ShouldConstrainToRootBounds = true;
        return SiteSettingsButton.IsEnabled && InvokeByAutomation(SiteSettingsButton);
    }

    public bool SiteSettingsFlyoutOpenForReview => SiteSettingsFlyout.IsOpen;

    public void HideSiteSettingsForReview() => SiteSettingsFlyout.Hide();

    // ── The notices above the page ───────────────────────────────────────

    /// <summary>Shows the "no secure connection" notice for the shown page as a
    /// failed https upgrade of <paramref name="http"/> would. The button is
    /// then pressed through its own peer.</summary>
    public void ShowNoSecureNoticeForReview(Uri http)
    {
        if (_shown is not { } panel) return;
        panel.UpgradedFrom = http;
        panel.Notice = PageNotice.NoSecureConnection;
        UpdateNotice();
    }

    /// <summary>Shows the "page stopped working" notice for the shown page as a
    /// failed render process would.</summary>
    public void ShowCrashedNoticeForReview()
    {
        if (_shown is not { } panel) return;
        panel.Notice = PageNotice.Crashed;
        UpdateNotice();
    }

    /// <summary>Asks the engine to end the shown page's render process
    /// (chrome://kill); the crash notice then comes from the engine's own event.</summary>
    public void KillRenderProcessForReview() => ShownCore?.Navigate("chrome://kill");

    public PageNotice NoticeForReview => _shown?.Notice ?? PageNotice.None;

    public bool NoticeOpenForReview => NoticeBar.IsOpen;

    public string NoticeButtonTextForReview => PageNoticeButton.Content as string ?? "";

    /// <summary>The notice's button, invoked as a click.</summary>
    public bool ClickNoticeButtonForReview() => NoticeBar.IsOpen && InvokeByAutomation(PageNoticeButton);

    /// <summary>Whether the engine allows http for a host from now on ("Continue anyway" kept).</summary>
    public bool HttpAllowedForReview(string host) => _https.Upgrade(new Uri($"http://{host}/")) is null;

    // ── The permission dialog ────────────────────────────────────────────

    /// <summary>The permission dialog's own Allow or Block button, found in the
    /// dialog's template and invoked as a click.</summary>
    public bool ClickPermissionButtonForReview(bool allow)
    {
        if (_permissionDialog is not { IsLoaded: true } dialog) return false;
        Button? button = FindAll<Button>(dialog).FirstOrDefault(b => b.Name == (allow ? "PrimaryButton" : "SecondaryButton"));
        return button is not null && InvokeByAutomation(button);
    }

    // ── UI Automation and the tree ───────────────────────────────────────

    private static AutomationPeer? PeerOf(UIElement element) =>
        FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);

    // The control's own invoke: a click for a button, a menu entry or a
    // container. False when the control is disabled or offers none.
    private static bool InvokeByAutomation(UIElement element)
    {
        if (element is Control { IsEnabled: false }) return false;
        AutomationPeer? peer = PeerOf(element);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
        else return false;
        return true;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) yield break;
        if (parent is T self) yield return self;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (T found in FindAll<T>(VisualTreeHelper.GetChild(parent, i))) yield return found;
    }

    private IReadOnlyList<Microsoft.UI.Xaml.Controls.Primitives.Popup> OpenPopups() =>
        Content?.XamlRoot is { } root ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root) : [];

    private List<MenuFlyoutPresenter> OpenMenuPresenters()
    {
        List<MenuFlyoutPresenter> found = [];
        foreach (Microsoft.UI.Xaml.Controls.Primitives.Popup popup in OpenPopups())
            found.AddRange(FindAll<MenuFlyoutPresenter>(popup.Child));
        return found;
    }

    private static IReadOnlyList<ReviewMenuEntry> Entries(MenuFlyoutPresenter presenter) =>
    [
        .. presenter.Items.OfType<MenuFlyoutItem>().Select(e => new ReviewMenuEntry(
            AutomationProperties.GetAutomationId(e), e.Text, e.IsEnabled, e.ActualHeight > 0 && e.Visibility == Visibility.Visible)),
    ];

    // A popup opens and lays out over the next frames.
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int waited = 0; !condition() && waited < 2000; waited += 50) await Task.Delay(50);
        await Task.Delay(100);
    }
}
